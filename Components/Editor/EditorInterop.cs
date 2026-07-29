namespace Fenrix_Terraform_UI.Components.Editor;

/// <summary>A diagnostic marker passed to the JS editor: 1-based line, severity ("error"/"warning"), message.
/// Serialised to the browser with camelCase (line/severity/message), matching fenrix-editor.js.</summary>
public sealed record EditorMarker(int Line, string Severity, string Message);

/// <summary>Result of a find / replace-and-find step returned from the JS editor.</summary>
public sealed record EditorFindResult(bool Found, int Count);

/// <summary>Result of a replace-all returned from the JS editor.</summary>
public sealed record EditorReplaceAllResult(int Count);
