// The wire-golden tests set the process current directory (so the server resolves responses/ + the spec DB)
// and each spins up a WebApplicationFactory on its own temp SQLite file. Run serially to keep the shared
// CWD and file I/O deterministic.
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]
