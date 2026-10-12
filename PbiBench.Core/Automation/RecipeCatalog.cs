using System;
using System.Collections.Generic;
using System.Linq;

namespace PbiBench.Core.Automation
{
    public enum RecipeValueKind { Text, Boolean, MultilineText }

    /// <summary>
    /// One reviewed, allowlisted recipe: a single measure property with a typed validator. Recipes never
    /// compile or run code; they produce an exact Before/After plan that the host applies in one undo batch.
    /// </summary>
    public sealed class RecipeDefinition
    {
        private readonly Func<string, string?> _validate;
        private readonly Func<string, string> _normalize;

        internal RecipeDefinition(string id, string title, string propertyName, MetadataRecipeOperation operation, RecipeValueKind valueKind,
            int maxLength, bool singleTargetOnly, string help, Func<string, string?> validate, Func<string, string>? normalize = null)
        {
            _normalize = normalize ?? (v => v);
            Id = id; Title = title; PropertyName = propertyName; Operation = operation; ValueKind = valueKind;
            MaxLength = maxLength; SingleTargetOnly = singleTargetOnly; Help = help; _validate = validate;
        }

        public string Id { get; }
        public string Title { get; }
        public string PropertyName { get; }
        public MetadataRecipeOperation Operation { get; }
        public RecipeValueKind ValueKind { get; }
        public int MaxLength { get; }
        public bool SingleTargetOnly { get; }
        public string Help { get; }

        /// <summary>Returns null when the value is acceptable, otherwise a user-facing reason.</summary>
        public string? Validate(string? value)
        {
            if (value == null) return "A value is required.";
            if (value.Length > MaxLength) return PropertyName + " is limited to " + MaxLength + " characters.";
            if (value.IndexOf('\0') >= 0) return PropertyName + " cannot contain NUL characters.";
            return _validate(value);
        }

        /// <summary>The exact value TE2 will store (e.g. descriptions are stored with LF line breaks only).</summary>
        public string Normalize(string value) => _normalize(value ?? "");

        public override string ToString() => Title;
    }

    /// <summary>The fixed catalog of reviewed measure recipes (AUTO-002). Extending it is a code review, not a user setting.</summary>
    public static class RecipeCatalog
    {
        private static readonly RecipeDefinition[] Definitions =
        {
            new RecipeDefinition("measure.displayFolder", "Set display folder", "DisplayFolder", MetadataRecipeOperation.SetDisplayFolder,
                RecipeValueKind.Text, 1024, false, "Backslash separates nested folders; empty clears the folder.", NoControlChars),
            new RecipeDefinition("measure.isHidden", "Set hidden state", "IsHidden", MetadataRecipeOperation.SetHidden,
                RecipeValueKind.Boolean, 5, false, "True hides the measure from report authors; False shows it.",
                v => v == "True" || v == "False" ? null : "Hidden must be True or False."),
            new RecipeDefinition("measure.formatString", "Set format string", "FormatString", MetadataRecipeOperation.SetFormatString,
                RecipeValueKind.Text, 512, false, "Static format string such as #,0.00 or 0.0%. Refused for measures with a format string expression; TE2 drops the legacy 'Format' annotation.",
                ValidateFormatString),
            new RecipeDefinition("measure.description", "Set description", "Description", MetadataRecipeOperation.SetDescription,
                RecipeValueKind.MultilineText, 4000, false, "Plain text shown as a tooltip; line breaks allowed (TE2 stores them as LF).",
                v => v.Any(c => char.IsControl(c) && c != '\r' && c != '\n' && c != '\t') ? "Description cannot contain control characters other than line breaks and tabs." : null,
                v => v.Replace("\r", "")),
            new RecipeDefinition("measure.rename", "Rename measure (with dependent preview)", "Name", MetadataRecipeOperation.Rename,
                RecipeValueKind.Text, 512, true, "Exactly one measure. Dependent expressions TE2 will rewrite are previewed; refused when a textual reference is not tracked by TE2.",
                ValidateName)
        };

        public static IReadOnlyList<RecipeDefinition> All { get; } = Array.AsReadOnly(Definitions);

        public static RecipeDefinition For(MetadataRecipeOperation operation) =>
            Definitions.FirstOrDefault(d => d.Operation == operation) ?? throw new ArgumentException("Recipe operation is not allowlisted.");

        public static RecipeDefinition? Find(string id) => Definitions.FirstOrDefault(d => string.Equals(d.Id, id, StringComparison.Ordinal));

        private static string? NoControlChars(string value) => value.Any(char.IsControl) ? "Control characters are not allowed." : null;

        private static string? ValidateFormatString(string value)
        {
            if (value.Any(char.IsControl)) return "Format string cannot contain control characters.";
            var quotes = 0;
            for (var i = 0; i < value.Length; i++)
            {
                if (value[i] == '\\') { i++; continue; }
                if (value[i] == '"') quotes++;
            }
            return quotes % 2 == 0 ? null : "Format string has an unbalanced double quote.";
        }

        private static string? ValidateName(string value)
        {
            if (value.Length == 0 || value.Trim().Length == 0) return "Name cannot be empty.";
            if (value.Trim() != value) return "Name cannot start or end with whitespace.";
            if (value.Any(char.IsControl)) return "Name cannot contain control characters.";
            return null;
        }
    }
}
