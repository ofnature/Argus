// VoyageMath's client-function overrides are process-wide, and one test installs a stub to prove they take
// precedence. Running test classes in parallel would let that stub leak into the formula tests.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
