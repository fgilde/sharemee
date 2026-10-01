using Coworkee.AuthServer;
using Coworkee.Core.Modularity;

namespace Shareme.Infrastructure;

[DependsOn(typeof(ShareMeInfrastructureModule), typeof(CoworkeeAuthStoreModule))]
public sealed class ShareMeDatabaseModule : CoworkeeModule;
