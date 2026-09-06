using System;
using System.Collections.Generic;
using System.Linq;

namespace PbiBench.Core.Navigation
{
    public enum QuickOpenMatchKind
    {
        None = 0,
        Subsequence = 1,
        Contains = 2,
        WordPrefix = 3,
        Prefix = 4,
        Exact = 5
    }

    public sealed class QuickOpenResult
    {
        public SemanticObjectDescriptor Object { get; set; } = new SemanticObjectDescriptor();
        public int Score { get; set; }
        public QuickOpenMatchKind MatchKind { get; set; }
    }

    /// <summary>
    /// Dependency-free Quick Open search/ranking. The caller supplies a projection of
    /// model objects and owns navigation to the selected result.
    ///
    /// Supported query examples:
    ///   revenue
    ///   net sales
    ///   kind:measure revenue
    ///   kind:table sales
    ///   kind:fn currency
    /// </summary>
    public static class QuickOpenMatcher
    {
        private static readonly Dictionary<string, SemanticObjectKind> KindAliases =
            new Dictionary<string, SemanticObjectKind>(StringComparer.OrdinalIgnoreCase)
            {
                ["model"] = SemanticObjectKind.Model,
                ["table"] = SemanticObjectKind.Table,
                ["t"] = SemanticObjectKind.Table,
                ["measure"] = SemanticObjectKind.Measure,
                ["m"] = SemanticObjectKind.Measure,
                ["column"] = SemanticObjectKind.Column,
                ["col"] = SemanticObjectKind.Column,
                ["c"] = SemanticObjectKind.Column,
                ["hierarchy"] = SemanticObjectKind.Hierarchy,
                ["h"] = SemanticObjectKind.Hierarchy,
                ["level"] = SemanticObjectKind.Level,
                ["relationship"] = SemanticObjectKind.Relationship,
                ["rel"] = SemanticObjectKind.Relationship,
                ["calculationgroup"] = SemanticObjectKind.CalculationGroup,
                ["calcgroup"] = SemanticObjectKind.CalculationGroup,
                ["cg"] = SemanticObjectKind.CalculationGroup,
                ["calculationitem"] = SemanticObjectKind.CalculationItem,
                ["calcitem"] = SemanticObjectKind.CalculationItem,
                ["ci"] = SemanticObjectKind.CalculationItem,
                ["function"] = SemanticObjectKind.Function,
                ["fn"] = SemanticObjectKind.Function,
                ["udf"] = SemanticObjectKind.Function,
                ["perspective"] = SemanticObjectKind.Perspective,
                ["role"] = SemanticObjectKind.Role,
                ["partition"] = SemanticObjectKind.Partition,
                ["part"] = SemanticObjectKind.Partition,
                ["datasource"] = SemanticObjectKind.DataSource,
                ["source"] = SemanticObjectKind.DataSource,
                ["ds"] = SemanticObjectKind.DataSource,
                ["other"] = SemanticObjectKind.Other
            };

        public static IReadOnlyList<QuickOpenResult> Search(
            IEnumerable<SemanticObjectDescriptor> objects,
            string query,
            int limit = 50)
        {
            if (objects == null) throw new ArgumentNullException(nameof(objects));
            if (limit <= 0) return Array.Empty<QuickOpenResult>();

            var parsed = ParseQuery(query ?? string.Empty);
            var results = new List<QuickOpenResult>();

            foreach (var item in objects)
            {
                if (item == null) continue;
                if (parsed.Kind.HasValue && item.Kind != parsed.Kind.Value) continue;

                var match = Score(item, parsed.Tokens);
                if (match == null) continue;

                results.Add(match);
            }

            return results
                .OrderByDescending(r => r.Score)
                .ThenBy(r => r.Object.IsHidden)
                .ThenBy(r => r.Object.Kind)
                .ThenBy(r => r.Object.EffectivePath, StringComparer.OrdinalIgnoreCase)
                .Take(limit)
                .ToArray();
        }

        private static QuickOpenResult? Score(SemanticObjectDescriptor item, string[] tokens)
        {
            if (tokens.Length == 0)
            {
                return new QuickOpenResult
                {
                    Object = item,
                    Score = KindBoost(item.Kind) - (item.IsHidden ? 50 : 0),
                    MatchKind = QuickOpenMatchKind.None
                };
            }

            var total = KindBoost(item.Kind);
            var strongest = QuickOpenMatchKind.None;

            foreach (var token in tokens)
            {
                var bestScore = 0;
                var bestKind = QuickOpenMatchKind.None;

                Consider(item.Name, token, 0, ref bestScore, ref bestKind);
                Consider(item.EffectivePath, token, 80, ref bestScore, ref bestKind);
                Consider(item.ParentName, token, 100, ref bestScore, ref bestKind);

                foreach (var searchTerm in item.SearchTerms)
                    Consider(searchTerm, token, 50, ref bestScore, ref bestKind);

                // Every token must match at least one searchable field. This makes multi-word
                // queries predictable and avoids high-scoring partial results.
                if (bestScore <= 0) return null;

                total += bestScore;
                if (bestKind > strongest) strongest = bestKind;
            }

            if (item.IsHidden) total -= 50;

            return new QuickOpenResult
            {
                Object = item,
                Score = total,
                MatchKind = strongest
            };
        }

        private static void Consider(
            string candidate,
            string token,
            int penalty,
            ref int bestScore,
            ref QuickOpenMatchKind bestKind)
        {
            QuickOpenMatchKind kind;
            var score = ScoreCandidate(candidate, token, penalty, out kind);
            if (score <= bestScore) return;

            bestScore = score;
            bestKind = kind;
        }

        private static int ScoreCandidate(
            string candidate,
            string token,
            int penalty,
            out QuickOpenMatchKind kind)
        {
            kind = QuickOpenMatchKind.None;
            if (string.IsNullOrWhiteSpace(candidate) || string.IsNullOrWhiteSpace(token)) return 0;

            candidate = candidate.Trim();
            token = token.Trim();

            if (string.Equals(candidate, token, StringComparison.OrdinalIgnoreCase))
            {
                kind = QuickOpenMatchKind.Exact;
                return Math.Max(1, 1000 - penalty);
            }

            if (candidate.StartsWith(token, StringComparison.OrdinalIgnoreCase))
            {
                kind = QuickOpenMatchKind.Prefix;
                return Math.Max(1, 850 - penalty);
            }

            if (HasWordPrefix(candidate, token))
            {
                kind = QuickOpenMatchKind.WordPrefix;
                return Math.Max(1, 760 - penalty);
            }

            if (candidate.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                kind = QuickOpenMatchKind.Contains;
                return Math.Max(1, 650 - penalty);
            }

            var fuzzy = SubsequenceScore(candidate, token);
            if (fuzzy > 0)
            {
                kind = QuickOpenMatchKind.Subsequence;
                return Math.Max(1, fuzzy - penalty);
            }

            return 0;
        }

        private static bool HasWordPrefix(string candidate, string token)
        {
            var start = 0;
            while (start < candidate.Length)
            {
                var index = candidate.IndexOf(token, start, StringComparison.OrdinalIgnoreCase);
                if (index < 0) return false;
                if (index == 0 || !char.IsLetterOrDigit(candidate[index - 1])) return true;
                start = index + 1;
            }

            return false;
        }

        private static int SubsequenceScore(string candidate, string token)
        {
            if (token.Length < 2 || token.Length > candidate.Length) return 0;

            var candidateUpper = candidate.ToUpperInvariant();
            var tokenUpper = token.ToUpperInvariant();
            var candidateIndex = 0;
            var firstMatch = -1;
            var lastMatch = -1;

            for (var tokenIndex = 0; tokenIndex < tokenUpper.Length; tokenIndex++)
            {
                var found = false;
                while (candidateIndex < candidateUpper.Length)
                {
                    if (candidateUpper[candidateIndex] == tokenUpper[tokenIndex])
                    {
                        if (firstMatch < 0) firstMatch = candidateIndex;
                        lastMatch = candidateIndex;
                        candidateIndex++;
                        found = true;
                        break;
                    }
                    candidateIndex++;
                }

                if (!found) return 0;
            }

            var span = lastMatch - firstMatch + 1;
            var gapPenalty = Math.Min(180, Math.Max(0, span - token.Length) * 12);
            var lengthPenalty = Math.Min(80, Math.Max(0, candidate.Length - token.Length) * 2);
            return Math.Max(120, 420 - gapPenalty - lengthPenalty);
        }

        private static int KindBoost(SemanticObjectKind kind)
        {
            switch (kind)
            {
                case SemanticObjectKind.Measure: return 35;
                case SemanticObjectKind.CalculationItem: return 30;
                case SemanticObjectKind.Function: return 30;
                case SemanticObjectKind.Table: return 25;
                case SemanticObjectKind.Column: return 20;
                case SemanticObjectKind.CalculationGroup: return 20;
                default: return 0;
            }
        }

        private static ParsedQuery ParseQuery(string query)
        {
            var searchTokens = new List<string>();
            SemanticObjectKind? kind = null;

            foreach (var token in query
                .Trim()
                .Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (token.StartsWith("kind:", StringComparison.OrdinalIgnoreCase))
                {
                    SemanticObjectKind parsedKind;
                    var alias = token.Substring("kind:".Length);
                    if (KindAliases.TryGetValue(alias, out parsedKind))
                    {
                        kind = parsedKind;
                        continue;
                    }
                }

                searchTokens.Add(token);
            }

            return new ParsedQuery(kind, searchTokens.ToArray());
        }

        private sealed class ParsedQuery
        {
            public ParsedQuery(SemanticObjectKind? kind, string[] tokens)
            {
                Kind = kind;
                Tokens = tokens;
            }

            public SemanticObjectKind? Kind { get; }
            public string[] Tokens { get; }
        }
    }
}
