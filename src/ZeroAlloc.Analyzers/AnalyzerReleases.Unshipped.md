### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
ZA0101 | Performance.Collections | Info | UseFrozenDictionaryAnalyzer
ZA0102 | Performance.Collections | Info | UseFrozenSetAnalyzer
ZA0103 | Performance.Collections | Info | UseCollectionsMarshalAsSpanAnalyzer
ZA0104 | Performance.Strings | Info | UseSearchValuesAnalyzer
ZA0105 | Performance.Collections | Warning | UseTryGetValueAnalyzer
ZA0106 | Performance.Collections | Warning | AvoidPrematureToListAnalyzer
ZA0201 | Performance.Strings | Warning | AvoidStringConcatInLoopAnalyzer
ZA0202 | Performance.Strings | Info | AvoidStringReplaceChainAnalyzer
ZA0301 | Performance.Memory | Info | UseStackallocAnalyzer
ZA0302 | Performance.Memory | Info | UseArrayPoolAnalyzer
ZA0203 | Performance.Strings | Info | UseSpanInsteadOfSubstringAnalyzer
ZA0401 | Performance.Logging | Info | UseLoggerMessageAnalyzer
ZA0501 | Performance.Boxing | Warning | AvoidBoxingInLoopsAnalyzer
ZA0502 | Performance.Boxing | Info | AvoidClosureInLoopsAnalyzer
ZA0503 | Performance.Boxing | Disabled | AvoidBoxingEverywhereAnalyzer
ZA0601 | Performance.Linq | Warning | AvoidLinqInLoopsAnalyzer
ZA0602 | Performance.Linq | Info | AvoidParamsInLoopsAnalyzer
ZA0701 | Performance.Regex | Info | UseGeneratedRegexAnalyzer
ZA0801 | Performance.Collections | Info | AvoidEnumHasFlagAnalyzer
ZA0802 | Performance.Enums | Info | AvoidEnumToStringAnalyzer
ZA0204 | Performance.Strings | Info | UseStringCreateAnalyzer
ZA0504 | Performance.Boxing | Info | AvoidDefensiveCopyAnalyzer
ZA0901 | Performance.Sealing | Info | ConsiderSealingClassAnalyzer
ZA1001 | Performance.Serialization | Info | UseJsonSourceGenerationAnalyzer
ZA0107 | Performance.Collections | Info | PreSizeCollectionsAnalyzer
ZA0205 | Performance.Strings | Info | UseCompositeFormatAnalyzer
ZA0206 | Performance.Strings | Info | AvoidSpanToStringBeforeParseAnalyzer
ZA0603 | Performance.Linq | Info | UseCountPropertyAnalyzer
ZA0604 | Performance.Linq | Info | UseCountOverAnyAnalyzer
ZA0605 | Performance.Linq | Info | UseIndexerOverLinqFirstAnalyzer
ZA0606 | Performance.Linq | Warning | AvoidForeachOverInterfaceCollectionAnalyzer
ZA0803 | Performance.Enums | Info | CacheEnumGetNameAnalyzer
ZA1101 | Performance.Async | Info | ElideAsyncAwaitAnalyzer
ZA1102 | Performance.Async | Info | DisposeCancellationTokenSourceAnalyzer
ZA1401 | Performance.Delegates | Info | UseStaticLambdaAnalyzer
ZA1501 | Performance.ValueTypes | Info | OverrideStructGetHashCodeAnalyzer
ZA1502 | Performance.ValueTypes | Info | AvoidFinalizersAnalyzer
ZA0208 | Performance.Strings | Warning | AvoidStringJoinBoxingOverloadAnalyzer
ZA0108 | Performance.Collections | Warning | AvoidRedundantMaterializationAnalyzer
ZA0109 | Performance.Collections | Warning | AvoidZeroLengthArrayAllocationAnalyzer
ZA0209 | Performance.Strings | Warning | AvoidValueTypeBoxingInStringConcatAnalyzer
ZA0607 | Performance.Linq | Warning | AvoidMultipleEnumerationAnalyzer
ZA1104 | Performance.Async | Warning | AvoidSpanInAsyncMethodAnalyzer
