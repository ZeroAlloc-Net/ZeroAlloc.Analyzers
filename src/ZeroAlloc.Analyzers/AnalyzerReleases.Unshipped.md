; Unshipped analyzer release.
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category                | Severity | Notes
--------|-------------------------|----------|----------------------------------------
ZA1503  | Performance.ValueTypes  | Info     | ImplementEquatableOnStructKeyAnalyzer
ZA1402  | Performance.Delegates   | Info     | UseStatePassingOverloadAnalyzer
ZA1706  | Performance.Aot         | Info     | MarkLibraryAotCompatibleAnalyzer
ZA1707  | Performance.Aot         | Info     | UseConfigurationBindingGeneratorAnalyzer
ZA1708  | Performance.Aot         | Info     | UseOptionsValidatorGeneratorAnalyzer
ZA1709  | Performance.Aot         | Info     | AvoidDynamicAnalyzer
