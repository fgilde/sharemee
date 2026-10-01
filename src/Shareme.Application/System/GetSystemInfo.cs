using System.Reflection;
using Coworkee.Application.Messaging;
using Coworkee.Core.Results;

namespace Shareme.Application.System;

public sealed record GetSystemInfo : IQuery<Result<SystemInfo>>;

public sealed record SystemInfo(string Product, string Version);

internal sealed class GetSystemInfoHandler : IHandler<GetSystemInfo, Result<SystemInfo>>
{
    private static readonly string Version =
        typeof(GetSystemInfoHandler).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";

    public Task<Result<SystemInfo>> HandleAsync(GetSystemInfo request, CancellationToken cancellationToken) =>
        Task.FromResult<Result<SystemInfo>>(new SystemInfo("Shareme", Version));
}
