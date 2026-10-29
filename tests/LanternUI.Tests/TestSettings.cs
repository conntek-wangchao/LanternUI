using Xunit;

// WPF gives one Application and one dispatcher per process, and a popup closes when another window
// takes the foreground. Tests that show windows therefore cannot run beside each other: they share
// focus, capture and the popup's lifetime. Serial, and honest about why.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
