# Local authoring ownership

TE2 Enhanced retains the upstream expression editor and highlighting. The pinned
`AntlrGrammars/DAXLexer.g4`, `DAXCharStream` and `DaxDependencyHelper.Tokenize` own
tokenization; no second grammar is introduced. TOMWrapper remains metadata,
semantic/dependency and navigation authority. There is no full local DAX parser
or engine certification. Unknown syntax and UDF binding are not diagnosed.

The Workbench Authoring menu provides an explicit Ctrl+Space insertion picker,
using TE2's function vocabulary and exact TOM DAX identifiers, including hidden
objects. It inserts at caret/replaces selection; it does not guess partial-token
replacement. Model snapshots rebuild for every invocation. Apply rejects changed
document identity/revision, removed/renamed objects and model replacement.
Go to object delegates to TE2 navigation. No remote service is used.

Ctrl+Shift+Space gives nested argument position plus a small curated signature
set. Ctrl+F7 checks matching parentheses/braces and selects the first issue.
Strings, quoted object names and comments use TE2 token classification and are
excluded from delimiter analysis. These checks do not validate names, types,
function availability, UDF semantics or incomplete lexical constructs. Assistance
is bounded to 256 Ki characters, 200 displayed suggestions and 100 issues.

DAX-010/011 are partial: query assistance is implemented; inline completion,
expression-editor integration, comprehensive signatures and richer diagnostics
remain deferred. The host smoke corpus exercises quotes/escaped names, nested
comments, variables, UDF syntax, malformed delimiters and metadata freshness.
