using Coworkee.Auditing;
using Coworkee.AuthServer;
using Coworkee.Core.Modularity;
using Coworkee.Mailing;
using Coworkee.Notifications;
using Coworkee.Theming;

namespace Shareme.Infrastructure;

[DependsOn(typeof(ShareMeInfrastructureModule), typeof(CoworkeeAuthStoreModule), typeof(CoworkeeMailingModule), typeof(CoworkeeAuditingModule), typeof(CoworkeeThemingModule), typeof(CoworkeeNotificationsModule), typeof(Coworkee.Account.CoworkeeAccountModule), typeof(Shareme.Graph.ShareMeGraphModule), typeof(Shareme.Metadata.ShareMeMetadataModule), typeof(Shareme.Assets.ShareMeAssetsModule), typeof(Shareme.Processing.ShareMeProcessingModule), typeof(Shareme.Search.ShareMeSearchModule))]
public sealed class ShareMeDatabaseModule : CoworkeeModule;
