using Coworkee.Auditing;
using Coworkee.AuthServer;
using Coworkee.Application.Messaging;
using Coworkee.Core.Modularity;
using Coworkee.Mailing;
using Coworkee.Notifications;
using Coworkee.Theming;
using Microsoft.Extensions.DependencyInjection;

namespace Shareme.Infrastructure;

[DependsOn(typeof(ShareMeInfrastructureModule), typeof(CoworkeeAuthStoreModule), typeof(CoworkeeMailingModule), typeof(CoworkeeAuditingModule), typeof(CoworkeeThemingModule), typeof(CoworkeeNotificationsModule), typeof(Coworkee.Account.CoworkeeAccountModule), typeof(Shareme.Graph.ShareMeGraphModule), typeof(Shareme.Metadata.ShareMeMetadataModule), typeof(Shareme.Assets.ShareMeAssetsModule), typeof(Shareme.Processing.ShareMeProcessingModule), typeof(Shareme.Search.ShareMeSearchModule), typeof(Shareme.Workspaces.ShareMeWorkspacesModule), typeof(Shareme.Automation.ShareMeAutomationModule), typeof(Shareme.Drm.ShareMeDrmModule), typeof(Shareme.Knowledge.ShareMeKnowledgeModule), typeof(Coworkee.Ai.CoworkeeAiModule), typeof(Coworkee.Localization.CoworkeeLocalizationModule), typeof(Coworkee.Backup.CoworkeeBackupModule), typeof(Coworkee.Chat.CoworkeeChatModule))]
public sealed class ShareMeDatabaseModule : CoworkeeModule
{
    public override void ConfigureServices(ModuleServiceContext context)
    {
        context.Services.AddMessagingFromAssembly(typeof(ShareMeDatabaseModule).Assembly);
        context.Services.AddAssistantTools();
        context.Services.AddSingleton<Coworkee.Localization.Resources.ILocalizationResourceContributor, Localization.SharemeTexts>();
    }
}
