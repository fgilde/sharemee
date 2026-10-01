using Coworkee.Auditing;
using Coworkee.AuthServer;
using Coworkee.Core.Modularity;
using Coworkee.Mailing;
using Coworkee.Notifications;
using Coworkee.Theming;

namespace Shareme.Infrastructure;

[DependsOn(typeof(ShareMeInfrastructureModule), typeof(CoworkeeAuthStoreModule), typeof(CoworkeeMailingModule), typeof(CoworkeeAuditingModule), typeof(CoworkeeThemingModule), typeof(CoworkeeNotificationsModule), typeof(Coworkee.Account.CoworkeeAccountModule))]
public sealed class ShareMeDatabaseModule : CoworkeeModule;
