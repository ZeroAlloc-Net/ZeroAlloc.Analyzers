; Unshipped analyzer release.
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category                | Severity | Notes
--------|-------------------------|----------|----------------------------------------
ZA0303  | Performance.Memory      | Warning  | ReturnRentedArrayAnalyzer
ZA0304  | Performance.Memory      | Info     | UseReadOnlySpanForConstantTableAnalyzer
ZA0210  | Performance.Strings     | Warning  | UseUtf8StringLiteralAnalyzer
ZA0111  | Performance.Collections | Info     | AvoidConcurrentDictionarySnapshotAnalyzer
ZA0212  | Performance.Strings     | Info     | UseTryParseAnalyzer
ZA0211  | Performance.Strings     | Info     | UseSpanSplitAnalyzer
ZA0110  | Performance.Collections | Info     | PreferParamsSpanAnalyzer
ZA1503  | Performance.ValueTypes  | Info     | ImplementEquatableOnStructKeyAnalyzer
ZA1402  | Performance.Delegates   | Info     | UseStatePassingOverloadAnalyzer
