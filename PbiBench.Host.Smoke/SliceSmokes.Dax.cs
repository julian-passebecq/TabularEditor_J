using System;
using TabularEditor.TOMWrapper;

// S2: DAX language service and model usage (DAX-010/011, MODEL-002).
internal static partial class SliceSmokes
{
    static partial void EarlyDax(TabularModelHandler handler)
    {
        ExpressionAuthoringSmoke.Run(handler);
        UnresolvedReferenceSmoke.Run(handler);
    }

    internal static void DaxCheck(bool condition, string message) { if (!condition) throw new Exception(message); }
}
