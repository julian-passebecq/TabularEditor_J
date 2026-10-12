using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using PbiBench.Core.Automation;
using TabularEditor.Scripting;
using TabularEditor.TOMWrapper;
using TabularEditor.UI;

namespace TabularEditor.PbiBench.Automation
{
    /// <summary>Compile output for one exact script version (document id + generation + content hash).</summary>
    internal sealed class PbiBenchScriptCompilation
    {
        internal PbiBenchScriptCompilation(ScriptDocument document, IReadOnlyList<ScriptDiagnostic> diagnostics, Action<Model, UITreeSelection> entry)
        {
            DocumentId = document.Id; Generation = document.Generation; ContentHash = document.ContentHash;
            Diagnostics = diagnostics; Entry = entry;
        }
        internal string DocumentId { get; }
        internal long Generation { get; }
        internal string ContentHash { get; }
        internal IReadOnlyList<ScriptDiagnostic> Diagnostics { get; }
        internal Action<Model, UITreeSelection> Entry { get; }
        internal bool Succeeded => Entry != null;
        internal bool IsFor(ScriptDocument document) =>
            document != null && document.Id == DocumentId && document.Generation == Generation && document.ContentHash == ContentHash;
    }

    internal enum PbiBenchScriptRunState { Succeeded, Failed, Cancelled }

    internal sealed class PbiBenchScriptRunOutcome
    {
        internal PbiBenchScriptRunOutcome(PbiBenchScriptRunState state, int modelChanges, bool rolledBack, string message, int line)
        { State = state; ModelChanges = modelChanges; RolledBack = rolledBack; Message = message; Line = line; }
        internal PbiBenchScriptRunState State { get; }
        internal int ModelChanges { get; }
        internal bool RolledBack { get; }
        internal string Message { get; }
        /// <summary>1-based script line of the failing statement when the stack trace exposes it, else 0.</summary>
        internal int Line { get; }
    }

    /// <summary>
    /// Thin adapter over TE2's own <see cref="ScriptEngine.CompileScript"/>: same preprocessing, references and
    /// compiler preferences as TE2's Advanced Scripting. Execution is trusted, in-process and NOT sandboxed; the only
    /// guarantees are consent (per-version trust), freshness (compile result must match the document version) and
    /// one TE2 undo batch for model changes. Effects outside the model (files, processes, network) cannot be undone.
    /// </summary>
    internal static class PbiBenchScriptCompiler
    {
        internal static PbiBenchScriptCompilation Compile(ScriptDocument document)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));
            var entry = ScriptEngine.CompileScript(document.Text, out var results);
            var messages = results.Errors.Cast<CompilerError>()
                .Select(e => new CompilerMessage(e.IsWarning, e.ErrorNumber, e.ErrorText, e.Line, e.Column));
            return new PbiBenchScriptCompilation(document, ScriptDiagnostics.FromCompiler(document, messages), entry);
        }

        internal static PbiBenchScriptRunOutcome Run(TabularModelHandler handler, ScriptDocument document, PbiBenchScriptCompilation compilation,
            UITreeSelection selection, bool rollbackOnError)
        {
            if (handler?.Model == null) throw new InvalidOperationException("No model is loaded.");
            if (compilation == null || !compilation.IsFor(document)) throw new InvalidOperationException("The script changed since it was compiled. Compile again.");
            if (!compilation.Succeeded) throw new InvalidOperationException("The script has compile errors.");
            if (!document.IsTrusted) throw new InvalidOperationException("Confirm that you trust this exact script version before running it.");
            if (!handler.UndoManager.Enabled || handler.UndoManager.BatchDepth != 0) throw new InvalidOperationException("TE2 is busy with another change. Try again.");

            handler.BeginUpdate("TE2 Enhanced script: " + document.Title);
            ScriptHelper.BeforeScriptExecution();
            try
            {
                compilation.Entry(handler.Model, selection);
                var count = handler.EndUpdateAll();
                return new PbiBenchScriptRunOutcome(PbiBenchScriptRunState.Succeeded, count, false, "Script finished.", 0);
            }
            catch (ScriptCanceledException)
            {
                var count = handler.EndUpdateAll(rollbackOnError);
                return new PbiBenchScriptRunOutcome(PbiBenchScriptRunState.Cancelled, count, rollbackOnError, "Script cancelled.", 0);
            }
            catch (Exception ex)
            {
                var count = handler.EndUpdateAll(rollbackOnError);
                return new PbiBenchScriptRunOutcome(PbiBenchScriptRunState.Failed, count, rollbackOnError,
                    ex.GetType().Name + ": " + ex.Message, ScriptLine(ex));
            }
            finally
            {
                try { handler.Model.Database?.CloseReader(); } catch (Exception) { }
                ScriptHelper.AfterScriptExecution();
            }
        }

        private static int ScriptLine(Exception ex)
        {
            var trace = new StackTrace(ex, true);
            for (var i = 0; i < trace.FrameCount; i++)
            {
                var frame = trace.GetFrame(i);
                if (frame.GetMethod()?.DeclaringType?.FullName == "TabularEditor.Scripting.ScriptHost") return Math.Max(0, frame.GetFileLineNumber());
            }
            return 0;
        }
    }
}
