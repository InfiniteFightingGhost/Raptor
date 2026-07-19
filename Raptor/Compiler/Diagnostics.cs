namespace Raptor.Compiler
{
    public enum DiagnosticSeverity
    {
        Warning,
        Error,
    }

    public record Diagnostic(
        string Code, // e.g., "E0001"
        DiagnosticSeverity Severity,
        string Message,
        int Line,
        int Column,
        int Length,
        string? Annotation = null // E.g., "expected ';'" or "type mismatch"
    );

    public class DiagnosticReporter
    {
        public List<Diagnostic> Diagnostics { get; } = new();
        public bool HasErrors => Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);

        public void Report(Diagnostic diagnostic) => Diagnostics.Add(diagnostic);
    }
}
