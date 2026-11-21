using FitFix.Api;

// FitFix API (Story 2.1). Everything the service is lives in FitFixApi, so the HTTP tests
// host this exact pipeline rather than a copy of it, and no endpoint exists here for their
// sake alone.
FitFixApi.Build(FitFixApi.CreateBuilder(args)).Run();
