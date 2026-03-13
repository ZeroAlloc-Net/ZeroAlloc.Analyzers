namespace ZeroAlloc.Analyzers;

public static class DiagnosticIds
{
    // ZA01xx — Collections
    public const string UseFrozenDictionary = "ZA0101";
    public const string UseFrozenSet = "ZA0102";
    public const string UseCollectionsMarshalAsSpan = "ZA0103";
    public const string UseSearchValues = "ZA0104";
    public const string UseTryGetValue = "ZA0105";
    public const string AvoidPrematureToList = "ZA0106";
    public const string PreSizeCollections = "ZA0107";
    public const string AvoidRedundantMaterialization = "ZA0108";
    public const string AvoidZeroLengthArrayAllocation = "ZA0109";

    // ZA02xx — Strings
    public const string AvoidStringConcatInLoop = "ZA0201";
    public const string AvoidStringReplaceChain = "ZA0202";
    public const string UseSpanInsteadOfSubstring = "ZA0203";
    public const string UseStringCreate = "ZA0204";
    public const string UseCompositeFormat = "ZA0205";
    public const string AvoidSpanToStringBeforeParse = "ZA0206";
    public const string AvoidStringJoinBoxingOverload = "ZA0208";
    public const string AvoidValueTypeBoxingInStringConcat = "ZA0209";

    // ZA03xx — Memory
    public const string UseStackalloc = "ZA0301";
    public const string UseArrayPool = "ZA0302";

    // ZA04xx — Logging
    public const string UseLoggerMessage = "ZA0401";

    // ZA05xx — Boxing & Structs
    public const string AvoidBoxingInLoops = "ZA0501";
    public const string AvoidClosureInLoops = "ZA0502";
    public const string AvoidBoxingEverywhere = "ZA0503";
    public const string AvoidDefensiveCopy = "ZA0504";

    // ZA06xx — LINQ & Params
    public const string AvoidLinqInLoops = "ZA0601";
    public const string AvoidParamsInLoops = "ZA0602";
    public const string UseCountProperty = "ZA0603";
    public const string UseCountOverAny = "ZA0604";
    public const string UseIndexerOverLinqFirst = "ZA0605";
    public const string AvoidForeachOverInterfaceCollection = "ZA0606";
    public const string AvoidMultipleEnumeration = "ZA0607";

    // ZA07xx — Regex
    public const string UseGeneratedRegex = "ZA0701";

    // ZA08xx — Enums
    public const string AvoidEnumHasFlag = "ZA0801";
    public const string AvoidEnumToString = "ZA0802";
    public const string CacheEnumGetName = "ZA0803";

    // ZA09xx — Sealing
    public const string ConsiderSealingClass = "ZA0901";

    // ZA10xx — Serialization
    public const string UseJsonSourceGeneration = "ZA1001";

    // ZA11xx — Async
    public const string ElideAsyncAwait = "ZA1101";
    public const string DisposeCancellationTokenSource = "ZA1102";
    // ZA1103 reserved — PreferValueTaskOverTask (dropped: not statically detectable)
    public const string AvoidSpanInAsyncMethod = "ZA1104";

    // ZA14xx — Delegates
    public const string UseStaticLambda = "ZA1401";

    // ZA15xx — Value Types
    public const string OverrideStructGetHashCode = "ZA1501";
    public const string AvoidFinalizers = "ZA1502";
}
