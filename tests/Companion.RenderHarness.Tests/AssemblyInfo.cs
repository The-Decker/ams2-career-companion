using Xunit;

// Run the render tests strictly SEQUENTIALLY. Every test hosts real WPF controls, which share
// process-wide static state — the single Application.Current, its merged theme ResourceDictionary,
// and (the one that actually bites) the CollectionView ViewManager that every ItemsControl touches
// to build a default view. xunit's default parallel-by-collection execution runs several of these
// on different STA threads at once, and the ViewManager's non-concurrent collections corrupt under
// concurrent access ("Operations that change non-concurrent collections must have exclusive
// access") — the intermittent multi-test failures the harness used to show. Serializing the whole
// assembly makes it deterministic; the harness is small, so the wall-clock cost is trivial.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
