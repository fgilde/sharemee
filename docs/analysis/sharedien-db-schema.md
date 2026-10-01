# Sharedien DB-Schema (aus bacpac `Sharedien3-2021-4-21-11-23.bacpac`)

Quelle: Azure SQL, Export 2021-04-21, DacFx 15.0. 142 Tabellen, 17 Views, 13 User-Defined Types.

## User-Defined Data Types

| Typ | Basis |
|---|---|
| MAM.utDate | date |
| MAM.utDateTime | datetime2 |
| MAM.utDescription | nvarchar(1024) |
| MAM.utEnum | int |
| MAM.utFilename | nvarchar(850) |
| MAM.utMutDate | datetime2 |
| MAM.utMutUser | nvarchar(200) |
| MAM.utName | nvarchar(850) |
| MAM.utNumber | int |
| MAM.utPublicIdentifier | nvarchar(200) |
| MAM.utSequenceNr | int |
| MAM.utTranslationIdentifier | nvarchar(80) |
| STH.utMutDate | datetime2 |

## Tabellen je Schema

- **Admin**: 32 Tabellen: Configuration, DerivationTypeRestriction, EntityPermission, ExpiringCode, ExternalGroup, ExternalGroupUserGroups, MaterializedNodePermission, NodePermission, PropertyDefinitionRestriction, PropertyValueRestriction, Role, RoleObjectType, RoleObjectTypePermission, Rule, RuleAction, RuleJob, RuleJobEntity, RuleJobEntityIdentifier, RuleJobEntityTypeIdentifier, RuleJobEntityValueExecuted, RuleJobRuleIdentifier, RuleNodeType, RuleTrigger, Tenant, User, UserGroup, UserGroupToRoleMap, UserGroupUser, UserNotificationSettings, UserPropertySet, UserRegistration, UserSettings
- **DataSync**: 4 Tabellen: provision_marker_dss, schema_info_dss, scope_config_dss, scope_info_dss
- **HangFire**: 11 Tabellen: AggregatedCounter, Counter, Hash, Job, JobParameter, JobQueue, List, Schema, Server, Set, State
- **MAM**: 85 Tabellen: ActivityLog, ActivityLogAdditionalData, ActivityLogUser, ArchiveExtractionJob, ArchiveExtractionJobEntry, AssetDerivation, AssetDerivationJob, AssetDerivationJobReport, AssetSet, AssetSetDuplicate, AssetType, AssetTypeDerivationType, AssetTypeProperty, AssetVersion, BackgroundJob, BackgroundJobTrigger, BillingContact, Buyout, Category, Comment, Currency, CustomNode, DbStatus, DerivationJobDefinition, DerivationJobDefinitionInputFileType, DerivationType, Edge, EdgeType, EdgeTypeAllowedNodeTypes, FileNamingDefinition, FileNamingDefinitionProperty, FileNamingDefinitionTenant, FileReference, FileType, FileTypeToDerivationType, Language, License, Node, NodeActivityLogSettings, NodeType, OutgoingEmail, OutgoingEmailRecipientAddress, OutgoingEmailRecipientUser, OutgoingEmailTemplateParameter, OutgoingMessageQueueEvent, PriceList, PriceListUserContext, Property, PropertyDefinition, PropertyDefinitionNodeType, PropertyDefinitionSystemPropertyStrategy, PropertyGroup, PropertyImport, PropertyImportEntity, PropertyImportMessage, PropertyImportMutation, PropertyListValue, PropertyMassEdit, PropertyMassEditAsset, PropertyMassEditMutation, PropertyMassEditResult, PropertyNodeTypeInheritance, PropertySet, PropertySetCondition, PropertySetPropertyDefinitions, PropertyValue, RightsOwner, SavedSearch, SearchReIndex, SearchReIndexFailedEntities, Share, ShareAssetTypeDerivationType, ShareRecipient, State, Task, TaskType, Translation, UploadSession, UploadSessionChunk, UserContext, UserContextGroup, UserContextGroupUserContext, WatermarkDefinition, WatermarkTrigger, Workspace
- **STH**: 5 Tabellen: Chunk, Data, Session, Storage, SyncTest
- **dbo**: 5 Tabellen: SchemaVersionsSharedien, SchemaVersionsStorageHandler, group_membership, groups, users

# Schema `Admin`


## Admin.Configuration

| Spalte | Typ | Null | Default / Computed |
|---|---|---|---|
| ConfigurationId | bigint | NOT NULL IDENTITY |  |
| Path | nvarchar(1536) | NOT NULL |  |
| Value | nvarchar(max) | NULL |  |
| MutUser | MAM.utMutUser=nvarchar(200) | NULL |  |
| MutDate | MAM.utMutDate=datetime2 | NULL |  |
| RowVersion | rowversion | NOT NULL |  |

- PK: ConfigurationId
- IX: UNIQUE Path

## Admin.DerivationTypeRestriction

| Spalte | Typ | Null | Default / Computed |
|---|---|---|---|
| DerivationTypeRestrictionId | bigint | NOT NULL IDENTITY |  |
| Identifier | MAM.utPublicIdentifier=nvarchar(200) | NOT NULL | (lower(CONVERT([nvarchar](50),newid()))) |
| RoleId | bigint | NOT NULL |  |
| Action | MAM.utEnum=int | NOT NULL |  |
| DerivationTypeId | bigint | NOT NULL |  |
| MutUser | MAM.utMutUser=nvarchar(200) | NULL |  |
| MutDate | MAM.utMutDate=datetime2 | NULL |  |
| RowVersion | rowversion | NOT NULL |  |
| AssetTypeId | bigint | NOT NULL |  |

- PK: DerivationTypeRestrictionId
- UNIQUE: Identifier
- FK: AssetTypeId -> MAM.AssetType(AssetTypeId)
- FK: DerivationTypeId -> MAM.DerivationType(DerivationTypeId)
- FK: RoleId -> Admin.Role(RoleId)

## Admin.EntityPermission

| Spalte | Typ | Null | Default / Computed |
|---|---|---|---|
| EntityPermissionId | bigint | NOT NULL IDENTITY |  |
| Identifier | MAM.utPublicIdentifier=nvarchar(200) | NOT NULL | (lower(CONVERT([nvarchar](50),newid()))) |
| SavedSearchId | bigint | NULL |  |
| PropertySetId | bigint | NULL |  |
| RoleId | bigint | NOT NULL |  |
| MutUser | MAM.utMutUser=nvarchar(200) | NOT NULL |  |
| MutDate | MAM.utMutDate=datetime2 | NOT NULL |  |
| RowVersion | rowversion | NOT NULL |  |
| AssetDerivationJobId | bigint | NULL |  |
| PropertyMassEditId | bigint | NULL |  |
| WorkspaceId | bigint | NULL |  |
| PropertyImportId | bigint | NULL |  |
| FileNamingDefinitionId | bigint | NULL |  |
| UserGroupId | bigint | NULL |  |
| UserId | bigint | NULL |  |
| ArchiveExtractionJobId | bigint | NULL |  |

- PK: EntityPermissionId
- UNIQUE: Identifier
- FK: ArchiveExtractionJobId -> MAM.ArchiveExtractionJob(ArchiveExtractionJobId)
- FK: AssetDerivationJobId -> MAM.AssetDerivationJob(AssetDerivationJobId)
- FK: FileNamingDefinitionId -> MAM.FileNamingDefinition(FileNamingDefinitionId)
- FK: PropertyImportId -> MAM.PropertyImport(PropertyImportId)
- FK: PropertyMassEditId -> MAM.PropertyMassEdit(PropertyMassEditId)
- FK: PropertySetId -> MAM.PropertySet(PropertySetId)
- FK: RoleId -> Admin.Role(RoleId)
- FK: WorkspaceId -> MAM.Workspace(WorkspaceId)
- FK: UserGroupId -> Admin.UserGroup(UserGroupId)
- FK: UserId -> Admin.User(UserId)
- FK: SavedSearchId -> MAM.SavedSearch(SavedSearchId)
- CHECK: [UserGroupId] IS NOT NULL AND [UserId] IS NULL OR [UserGroupId] IS NULL AND [UserId] IS NOT NULL
- CHECK: (((((((case when [SavedSearchId] IS NULL then (0) else (1) end+case when [PropertySetId] IS NULL then (0) else (1) end)+case when [WorkspaceId] IS NULL then (0) else (1) end)+case when [AssetDerivationJobId] IS NULL then (0) else (1) end)+case when [PropertyMassEditId] IS NULL then (0) else (1) end)+case when [PropertyImportId] IS NULL then (0) else (1) end)+case when [FileNamingDefinitionId] IS NULL then (0) else (1) end)+case when [ArchiveExtractionJobId] IS NULL then (0) else (1) end)=(1)

## Admin.ExpiringCode

| Spalte | Typ | Null | Default / Computed |
|---|---|---|---|
| ExpiringCodeId | bigint | NOT NULL IDENTITY |  |
| UserId | bigint | NOT NULL |  |
| Code | nvarchar(200) | NOT NULL |  |
| CodeType | MAM.utEnum=int | NOT NULL |  |
| ExpiresAt | MAM.utDateTime=datetime2 | NOT NULL |  |
| MutUser | MAM.utMutUser=nvarchar(200) | NULL |  |
| MutDate | MAM.utMutDate=datetime2 | NULL |  |
| RowVersion | rowversion | NOT NULL |  |
| CreatedAt | datetime2 | NOT NULL | (getutcdate()) |
- FK: UserId -> Admin.User(UserId)
- IX: UNIQUE UserId,CodeType

## Admin.ExternalGroup

| Spalte | Typ | Null | Default / Computed |
|---|---|---|---|
| ExternalGroupId | bigint | NOT NULL IDENTITY |  |
| Identifier | MAM.utPublicIdentifier=nvarchar(200) | NOT NULL |  |
| ExternalGroupName | nvarchar(200) | NOT NULL |  |
| MutUser | MAM.utMutUser=nvarchar(200) | NULL |  |
| MutDate | MAM.utMutDate=datetime2 | NULL |  |
| RowVersion | rowversion | NOT NULL |  |
| ExternalGroupDisplayName | nvarchar(200) | NULL |  |

- PK: ExternalGroupId
- UNIQUE: Identifier

## Admin.ExternalGroupUserGroups

| Spalte | Typ | Null | Default / Computed |
|---|---|---|---|
| ExternalGroupUserGroupsId | bigint | NOT NULL IDENTITY |  |
| ExternalGroupId | bigint | NOT NULL |  |
| UserGroupId | bigint | NOT NULL |  |
| MutUser | MAM.utMutUser=nvarchar(200) | NULL |  |
| MutDate | MAM.utMutDate=datetime2 | NULL |  |
| RowVersion | rowversion | NOT NULL |  |

- PK: ExternalGroupUserGroupsId
- FK: ExternalGroupId -> Admin.ExternalGroup(ExternalGroupId)
- FK: UserGroupId -> Admin.UserGroup(UserGroupId)

## Admin.MaterializedNodePermission

| Spalte | Typ | Null | Default / Computed |
|---|---|---|---|
| MaterializedNodePermissionId | bigint | NOT NULL IDENTITY |  |
| NodeId | bigint | NOT NULL |  |
| NodePermissionId | bigint | NOT NULL |  |
| MutUser | MAM.utMutUser=nvarchar(200) | NULL |  |
| MutDate | MAM.utMutDate=datetime2 | NULL |  |
| RowVersion | rowversion | NOT NULL |  |

- PK: MaterializedNodePermissionId
- FK: NodeId -> MAM.Node(NodeId)
- FK: NodePermissionId -> Admin.NodePermission(NodePermissionId)
- IX: NodeId INCLUDE(NodePermissionId)

## Admin.NodePermission

| Spalte | Typ | Null | Default / Computed |
|---|---|---|---|
| NodePermissionId | bigint | NOT NULL IDENTITY |  |
| Identifier | MAM.utPublicIdentifier=nvarchar(200) | NOT NULL | (lower(CONVERT([nvarchar](50),newid()))) |
| NodeId | bigint | NOT NULL |  |
| RoleId | bigint | NOT NULL |  |
| Status | MAM.utEnum=int | NOT NULL |  |
| MutUser | MAM.utMutUser=nvarchar(200) | NULL |  |
| MutDate | MAM.utMutDate=datetime2 | NULL |  |
| RowVersion | rowversion | NOT NULL |  |
| UserGroupId | bigint | NULL |  |
| UserId | bigint | NULL |  |

- PK: NodePermissionId
- UNIQUE: Identifier
- FK: UserGroupId -> Admin.UserGroup(UserGroupId)
- FK: UserId -> Admin.User(UserId)
- FK: NodeId -> MAM.Node(NodeId)
- FK: RoleId -> Admin.Role(RoleId)
- IX: UNIQUE NodeId,UserGroupId,UserId,RoleId
- CHECK: [UserGroupId] IS NOT NULL AND [UserId] IS NULL OR [UserGroupId] IS NULL AND [UserId] IS NOT NULL

## Admin.PropertyDefinitionRestriction

| Spalte | Typ | Null | Default / Computed |
|---|---|---|---|
| PropertyDefinitionRestrictionId | bigint | NOT NULL IDENTITY |  |
| Identifier | MAM.utPublicIdentifier=nvarchar(200) | NOT NULL | (lower(CONVERT([nvarchar](50),newid()))) |
| RoleId | bigint | NOT NULL |  |
| Status | MAM.utEnum=int | NOT NULL |  |
| Action | MAM.utEnum=int | NOT NULL |  |
| PropertyDefinitionId | bigint | NOT NULL |  |
| MutUser | MAM.utMutUser=nvarchar(200) | NULL |  |
| MutDate | MAM.utMutDate=datetime2 | NULL |  |
| RowVersion | rowversion | NOT NULL |  |

- PK: PropertyDefinitionRestrictionId
- UNIQUE: Identifier
- FK: PropertyDefinitionId -> MAM.PropertyDefinition(PropertyDefinitionId)
- FK: RoleId -> Admin.Role(RoleId)

## Admin.PropertyValueRestriction

| Spalte | Typ | Null | Default / Computed |
|---|---|---|---|
| PropertyValueRestrictionId | bigint | NOT NULL IDENTITY |  |
| RoleId | bigint | NOT NULL |  |
| Action | MAM.utEnum=int | NOT NULL |  |
| PropertyId | bigint | NOT NULL |  |
| MutUser | MAM.utMutUser=nvarchar(200) | NULL |  |
| MutDate | MAM.utMutDate=datetime2 | NULL |  |
| RowVersion | rowversion | NOT NULL |  |

- PK: PropertyValueRestrictionId
- UNIQUE: RoleId, Action, PropertyId

## Admin.Role

| Spalte | Typ | Null | Default / Computed |
|---|---|---|---|
| RoleId | bigint | NOT NULL IDENTITY |  |
| Identifier | MAM.utPublicIdentifier=nvarchar(200) | NOT NULL | (lower(CONVERT([nvarchar](50),newid()))) |
| TranslationIdentifier | MAM.utTranslationIdentifier=nvarchar(80) | NOT NULL |  |
| Status | MAM.utEnum=int | NOT NULL |  |
| IsDeleted | bit | NOT NULL |  |
| MutUser | MAM.utMutUser=nvarchar(200) | NULL |  |
| MutDate | MAM.utMutDate=datetime2 | NULL |  |
| RowVersion | rowversion | NOT NULL |  |
| IsSystem | bit | NOT NULL | ((0)) |
| PropertyMassEditEntityLimit | int | NULL |  |
| PropertyExportEntityLimit | int | NULL |  |

- PK: RoleId
- IX: UNIQUE Identifier INCLUDE(IsDeleted) WHERE ([IsDeleted]<>(1))

## Admin.RoleObjectType

| Spalte | Typ | Null | Default / Computed |
|---|---|---|---|
| RoleObjectTypeId | bigint | NOT NULL IDENTITY |  |
| RoleId | bigint | NOT NULL |  |
| ObjectType | MAM.utEnum=int | NOT NULL |  |

- PK: RoleObjectTypeId
- UNIQUE: RoleId, ObjectType
- FK: RoleId -> Admin.Role(RoleId)

## Admin.RoleObjectTypePermission

| Spalte | Typ | Null | Default / Computed |
|---|---|---|---|
| RoleObjectTypePermissionId | bigint | NOT NULL IDENTITY |  |
| RoleId | bigint | NOT NULL |  |
| MutUser | MAM.utMutUser=nvarchar(200) | NULL |  |
| MutDate | MAM.utMutDate=datetime2 | NULL |  |
| RowVersion | rowversion | NOT NULL |  |
| Permission | int | NOT NULL | ((0)) |

- PK: RoleObjectTypePermissionId
- FK: RoleId -> Admin.Role(RoleId)
- IX: Permission
- IX: RoleId

## Admin.Rule

| Spalte | Typ | Null | Default / Computed |
|---|---|---|---|
| RuleId | bigint | NOT NULL IDENTITY |  |
| Identifier | MAM.utPublicIdentifier=nvarchar(200) | NOT NULL | (lower(CONVERT([nvarchar],newid()))) |
| TranslationIdentifier | MAM.utTranslationIdentifier=nvarchar(80) | NOT NULL |  |
| MutUser | MAM.utMutUser=nvarchar(200) | NULL |  |
| MutDate | MAM.utMutDate=datetime2 | NULL |  |
| RowVersion | rowversion | NOT NULL |  |

- PK: RuleId
- UNIQUE: Identifier

## Admin.RuleAction

| Spalte | Typ | Null | Default / Computed |
|---|---|---|---|
| RuleActionId | bigint | NOT NULL IDENTITY |  |
| RuleId | bigint | NOT NULL |  |
| PropertyId | bigint | NULL |  |
| OnTriggerMatched | bit | NOT NULL |  |
| MutUser | MAM.utMutUser=nvarchar(200) | NULL |  |
| MutDate | MAM.utMutDate=datetime2 | NULL |  |
| RowVersion | rowversion | NOT NULL |  |
| AdditionalData | varchar(max) | NULL |  |
| Template | varchar(max) | NULL |  |

- PK: RuleActionId
- FK: PropertyId -> MAM.Property(PropertyId)
- FK: RuleId -> Admin.Rule(RuleId)

## Admin.RuleJob

| Spalte | Typ | Null | Default / Computed |
|---|---|---|---|
| RuleJobId | bigint | NOT NULL IDENTITY |  |
| Identifier | MAM.utPublicIdentifier=nvarchar(200) | NOT NULL |  |
| Status | MAM.utEnum=int | NULL |  |
| CreatedAt | MAM.utDateTime=datetime2 | NULL |  |
| InitiatedBy | MAM.utMutUser=nvarchar(200) | NULL |  |
| InitiatedAt | MAM.utDateTime=datetime2 | NULL |  |
| FinishedAt | MAM.utDateTime=datetime2 | NULL |  |
| BackgroundJobId | bigint | NULL |  |
| CurrentCount | int | NULL |  |
| MatchedTriggerCount | int | NULL |  |
| NotMatchedTriggerCount | int | NULL |  |
| ErrorCount | int | NULL |  |
| TotalCount | int | NULL |  |
| MutDate | MAM.utMutDate=datetime2 | NULL |  |
| RowVersion | rowversion | NULL |  |
| MatchedActionExecutedCount | int | NULL |  |
| NotMatchedActionExecutedCount | int | NULL |  |
| ActionSkippedCount | int | NULL |  |

- PK: RuleJobId

## Admin.RuleJobEntity

| Spalte | Typ | Null | Default / Computed |
|---|---|---|---|
| RuleJobEntityId | bigint | NOT NULL IDENTITY |  |
| RuleJobId | bigint | NOT NULL |  |
| NodeId | bigint | NOT NULL |  |
| RuleId | bigint | NOT NULL |  |
| UserContextId | bigint | NULL |  |
| IsMatchedTrigger | bit | NULL |  |
| IsError | bit | NULL |  |
| ErrorReason | nvarchar(max) | NULL |  |
| IsMatchedActionExecuted | bit | NULL |  |
| IsNotMatchedActionExecuted | bit | NULL |  |
| IsProcessed | bit | NULL |  |

- PK: RuleJobEntityId
- FK: NodeId -> MAM.Node(NodeId)
- FK: RuleId -> Admin.Rule(RuleId)
- FK: RuleJobId -> Admin.RuleJob(RuleJobId)
- FK: UserContextId -> MAM.UserContext(UserContextId)

## Admin.RuleJobEntityIdentifier

| Spalte | Typ | Null | Default / Computed |
|---|---|---|---|
| RuleJobEntityIdentifierId | bigint | NOT NULL IDENTITY |  |
| RuleJobId | bigint | NOT NULL |  |
| EntityIdentifier | MAM.utPublicIdentifier=nvarchar(200) | NOT NULL |  |

- PK: RuleJobEntityIdentifierId
- FK: RuleJobId -> Admin.RuleJob(RuleJobId)

## Admin.RuleJobEntityTypeIdentifier

| Spalte | Typ | Null | Default / Computed |
|---|---|---|---|
| RuleJobEntityTypeIdentifierId | bigint | NOT NULL IDENTITY |  |
| RuleJobId | bigint | NOT NULL |  |
| EntityTypeIdentifier | MAM.utPublicIdentifier=nvarchar(200) | NOT NULL |  |

- PK: RuleJobEntityTypeIdentifierId
- FK: RuleJobId -> Admin.RuleJob(RuleJobId)

## Admin.RuleJobEntityValueExecuted

| Spalte | Typ | Null | Default / Computed |
|---|---|---|---|
| RuleJobEntityValueExecutedId | bigint | NOT NULL IDENTITY |  |
| RuleJobEntityId | bigint | NULL |  |
| Identifier | MAM.utPublicIdentifier=nvarchar(200) | NULL |  |
| ValueExecuted | varchar(max) | NULL |  |

- PK: RuleJobEntityValueExecutedId
- FK: RuleJobEntityId -> Admin.RuleJobEntity(RuleJobEntityId)

## Admin.RuleJobRuleIdentifier

| Spalte | Typ | Null | Default / Computed |
|---|---|---|---|
| RuleJobEntityIdentifierId | bigint | NOT NULL IDENTITY |  |
| RuleJobId | bigint | NOT NULL |  |
| RuleIdentifier | MAM.utPublicIdentifier=nvarchar(200) | NOT NULL |  |

- PK: RuleJobEntityIdentifierId
- FK: RuleJobId -> Admin.RuleJob(RuleJobId)

## Admin.RuleNodeType

| Spalte | Typ | Null | Default / Computed |
|---|---|---|---|
| RuleNodeTypeId | bigint | NOT NULL IDENTITY |  |
| RuleId | bigint | NOT NULL |  |
| NodeTypeId | bigint | NOT NULL |  |
- FK: NodeTypeId -> MAM.NodeType(NodeTypeId)
- FK: RuleId -> Admin.Rule(RuleId)

## Admin.RuleTrigger

| Spalte | Typ | Null | Default / Computed |
|---|---|---|---|
| RuleTriggerId | bigint | NOT NULL IDENTITY |  |
| RuleId | bigint | NOT NULL |  |
| IsFilter | bit | NOT NULL |  |
| TriggerOperator | int | NOT NULL |  |
| PropertyId | bigint | NULL |  |
| AdditionalData | varchar(max) | NULL |  |
| MutUser | nvarchar(200) | NULL |  |
| MutDate | datetime2 | NULL |  |
| RowVersion | rowversion | NOT NULL |  |

- PK: RuleTriggerId
- FK: PropertyId -> MAM.Property(PropertyId)
- FK: RuleId -> Admin.Rule(RuleId)

## Admin.Tenant

| Spalte | Typ | Null | Default / Computed |
|---|---|---|---|
| TenantId | bigint | NOT NULL IDENTITY |  |
| Identifier | MAM.utPublicIdentifier=nvarchar(200) | NOT NULL | (lower(CONVERT([nvarchar](50),newid()))) |
| Status | MAM.utEnum=int | NOT NULL |  |
| IsDefaultTenant | bit | NOT NULL |  |
| TranslationIdentifier | MAM.utTranslationIdentifier=nvarchar(80) | NOT NULL |  |
| IsDeleted | bit | NOT NULL |  |
| MutUser | MAM.utMutUser=nvarchar(200) | NULL |  |
| MutDate | MAM.utMutDate=datetime2 | NULL |  |
| RowVersion | rowversion | NOT NULL |  |
| AcceptsRegistrations | bit | NOT NULL | ((0)) |

- PK: TenantId
- IX: UNIQUE Identifier INCLUDE(IsDeleted) WHERE ([IsDeleted]<>(1))

## Admin.User

| Spalte | Typ | Null | Default / Computed |
|---|---|---|---|
| UserId | bigint | NOT NULL IDENTITY |  |
| Identifier | MAM.utPublicIdentifier=nvarchar(200) | NOT NULL | (lower(CONVERT([nvarchar](50),newid()))) |
| UserName | MAM.utName=nvarchar(850) | NOT NULL |  |
| FirstName | MAM.utName=nvarchar(850) | NULL |  |
| LastName | MAM.utName=nvarchar(850) | NULL |  |
| EmailAddress | nvarchar(320) | NULL |  |
| Department | MAM.utName=nvarchar(850) | NULL |  |
| Status | MAM.utEnum=int | NOT NULL |  |
| IsDeleted | bit | NOT NULL |  |
| PasswordSalt | nvarchar(1024) | NOT NULL |  |
| PasswordHash | nvarchar(max) | NOT NULL |  |
| ProviderName | nvarchar(128) | NOT NULL |  |
| ProviderSubjectId | nvarchar(max) | NULL |  |
| MutUser | MAM.utMutUser=nvarchar(200) | NULL |  |
| MutDate | MAM.utMutDate=datetime2 | NULL |  |
| RowVersion | rowversion | NOT NULL |  |
| LastLoginAttemptDate | MAM.utDateTime=datetime2 | NULL | (NULL) |
| LastLoginDate | MAM.utDateTime=datetime2 | NULL | (NULL) |
| LastPasswordResetDate | MAM.utDateTime=datetime2 | NULL | (NULL) |
| CreatedAt | MAM.utDateTime=datetime2 | NOT NULL | (getutcdate()) |
| Company | nvarchar(250) | NULL |  |
| ContactPerson | nvarchar(150) | NULL |  |
| PhoneNumber | nvarchar(100) | NULL |  |
| StatusChangeDate | datetime2 | NOT NULL | (getutcdate()) |
| AccessType | MAM.utEnum=int | NOT NULL |  |

- PK: UserId
- IX: UNIQUE UserName,ProviderName INCLUDE(IsDeleted) WHERE ([IsDeleted]<>(1))

## Admin.UserGroup

| Spalte | Typ | Null | Default / Computed |
|---|---|---|---|
| UserGroupId | bigint | NOT NULL IDENTITY |  |
| Identifier | MAM.utPublicIdentifier=nvarchar(200) | NOT NULL | (lower(CONVERT([nvarchar](50),newid()))) |
| Name | MAM.utName=nvarchar(850) | NOT NULL |  |
| Description | nvarchar(2048) | NULL |  |
| IsDeleted | bit | NOT NULL |  |
| MutUser | MAM.utMutUser=nvarchar(200) | NULL |  |
| MutDate | MAM.utMutDate=datetime2 | NULL |  |
| RowVersion | rowversion | NOT NULL |  |

- PK: UserGroupId
- IX: UNIQUE Identifier INCLUDE(IsDeleted) WHERE ([IsDeleted]<>(1))

## Admin.UserGroupToRoleMap

| Spalte | Typ | Null | Default / Computed |
|---|---|---|---|
| UserGroupToRoleMapId | bigint | NOT NULL IDENTITY |  |
| TenantId | bigint | NOT NULL |  |
| RoleId | bigint | NOT NULL |  |
| MutUser | MAM.utMutUser=nvarchar(200) | NULL |  |
| MutDate | MAM.utMutDate=datetime2 | NULL |  |
| RowVersion | rowversion | NOT NULL |  |
| UserGroupId | bigint | NOT NULL |  |

- PK: UserGroupToRoleMapId
- UNIQUE: TenantId, RoleId, UserGroupId
- FK: UserGroupId -> Admin.UserGroup(UserGroupId)
- FK: RoleId -> Admin.Role(RoleId)
- FK: TenantId -> Admin.Tenant(TenantId)

## Admin.UserGroupUser

| Spalte | Typ | Null | Default / Computed |
|---|---|---|---|
| UserGroupUserId | bigint | NOT NULL IDENTITY |  |
| UserGroupId | bigint | NOT NULL |  |
| UserId | bigint | NOT NULL |  |
| IsProvisioned | bit | NOT NULL |  |
| MutUser | MAM.utMutUser=nvarchar(200) | NULL |  |
| MutDate | MAM.utMutDate=datetime2 | NULL |  |

- PK: UserGroupUserId
- UNIQUE: UserGroupId, UserId
- FK: UserGroupId -> Admin.UserGroup(UserGroupId)
- FK: UserId -> Admin.User(UserId)
- IX: UserId INCLUDE(UserGroupId)

## Admin.UserNotificationSettings

| Spalte | Typ | Null | Default / Computed |
|---|---|---|---|
| UserNotificationSettingsId | bigint | NOT NULL IDENTITY |  |
| NodeId | bigint | NOT NULL |  |
| DisableNotifications | bit | NOT NULL |  |
| MutUser | MAM.utMutUser=nvarchar(200) | NOT NULL |  |
| MutDate | MAM.utMutDate=datetime2 | NOT NULL |  |
| RowVersion | rowversion | NOT NULL |  |
| UserId | bigint | NULL |  |

- PK: UserNotificationSettingsId
- FK: UserId -> Admin.User(UserId)
- FK: NodeId -> MAM.Node(NodeId)

## Admin.UserPropertySet

| Spalte | Typ | Null | Default / Computed |
|---|---|---|---|
| UserPropertySetId | bigint | NOT NULL IDENTITY |  |
| Identifier | MAM.utPublicIdentifier=nvarchar(200) | NOT NULL | (lower(CONVERT([nvarchar](50),newid()))) |
| UserId | bigint | NOT NULL |  |
| PropertyId | bigint | NOT NULL |  |
| CanWrite | bit | NOT NULL |  |
| MutUser | MAM.utMutUser=nvarchar(200) | NULL |  |
| MutDate | MAM.utMutDate=datetime2 | NULL |  |
| RowVersion | rowversion | NOT NULL |  |

- PK: UserPropertySetId
- UNIQUE: Identifier
- FK: UserId -> Admin.User(UserId)
- FK: PropertyId -> MAM.Property(PropertyId)
- IX: PropertyId
- IX: UserId

## Admin.UserRegistration

| Spalte | Typ | Null | Default / Computed |
|---|---|---|---|
| UserRegistrationId | bigint | NOT NULL IDENTITY |  |
| UserId | bigint | NOT NULL |  |
| TenantIdentifier | MAM.utPublicIdentifier=nvarchar(200) | NULL |  |
| MutUser | MAM.utMutUser=nvarchar(200) | NULL |  |
| MutDate | MAM.utMutDate=datetime2 | NULL |  |
| RowVersion | rowversion | NOT NULL |  |
| ApprovedAt | MAM.utDateTime=datetime2 | NULL |  |
- FK: UserId -> Admin.User(UserId)

## Admin.UserSettings

| Spalte | Typ | Null | Default / Computed |
|---|---|---|---|
| UserSettingId | bigint | NOT NULL IDENTITY |  |
| DisplayLanguage | nvarchar(50) | NULL |  |
| Locale | nvarchar(50) | NULL |  |
| LanguageId | bigint | NULL |  |
| UiSettings | nvarchar(max) | NULL |  |
| MutUser | MAM.utMutUser=nvarchar(200) | NULL |  |
| MutDate | MAM.utMutDate=datetime2 | NULL |  |
| RowVersion | rowversion | NOT NULL |  |
| UserContextId | bigint | NULL |  |
| UserId | bigint | NOT NULL |  |
| AvatarFileReferenceId | bigint | NULL |  |
| SearchTermOperator | MAM.utEnum=int | NULL |  |

- PK: UserSettingId
- UNIQUE: UserId
- FK: UserId -> Admin.User(UserId)
- FK: LanguageId -> MAM.Language(LanguageId)
- FK: AvatarFileReferenceId -> MAM.FileReference(FileReferenceId)
- FK: UserContextId -> MAM.UserContext(UserContextId)

# Schema `DataSync`


## DataSync.provision_marker_dss

| Spalte | Typ | Null | Default / Computed |
|---|---|---|---|
| object_id | int | NOT NULL |  |
| owner_scope_local_id | int | NOT NULL |  |
| provision_scope_local_id | int | NULL |  |
| provision_timestamp | bigint | NOT NULL |  |
| provision_local_peer_key | int | NOT NULL |  |
| provision_scope_peer_key | int | NULL |  |
| provision_scope_peer_timestamp | bigint | NULL |  |
| provision_datetime | datetime | NULL |  |
| state | int | NULL |  |
| version | rowversion | NOT NULL |  |

- PK: owner_scope_local_id, object_id

## DataSync.schema_info_dss

| Spalte | Typ | Null | Default / Computed |
|---|---|---|---|
| schema_major_version | int | NOT NULL |  |
| schema_minor_version | int | NOT NULL |  |
| schema_extended_info | nvarchar(100) | NOT NULL |  |

- PK: schema_major_version, schema_minor_version

## DataSync.scope_config_dss

| Spalte | Typ | Null | Default / Computed |
|---|---|---|---|
| config_id | uniqueidentifier | NOT NULL |  |
| config_data | xml | NOT NULL |  |
| scope_status | char(1) | NULL |  |

- PK: config_id

## DataSync.scope_info_dss

| Spalte | Typ | Null | Default / Computed |
|---|---|---|---|
| scope_local_id | int | NOT NULL IDENTITY |  |
| scope_id | uniqueidentifier | NOT NULL | (newid()) |
| sync_scope_name | nvarchar(100) | NOT NULL |  |
| scope_sync_knowledge | varbinary(max) | NULL |  |
| scope_tombstone_cleanup_knowledge | varbinary(max) | NULL |  |
| scope_timestamp | rowversion | NULL |  |
| scope_config_id | uniqueidentifier | NULL |  |
| scope_restore_count | int | NOT NULL | ((0)) |
| scope_user_comment | nvarchar(max) | NULL |  |

- PK: sync_scope_name

# Schema `HangFire`


## HangFire.AggregatedCounter

| Spalte | Typ | Null | Default / Computed |
|---|---|---|---|
| Id | int | NOT NULL IDENTITY |  |
| Key | nvarchar(100) | NOT NULL |  |
| Value | bigint | NOT NULL |  |
| ExpireAt | datetime | NULL |  |

- PK: Id
- IX: UNIQUE Key INCLUDE(Value)

## HangFire.Counter

| Spalte | Typ | Null | Default / Computed |
|---|---|---|---|
| Id | int | NOT NULL IDENTITY |  |
| Key | nvarchar(100) | NOT NULL |  |
| Value | smallint | NOT NULL |  |
| ExpireAt | datetime | NULL |  |

- PK: Id
- IX: Key INCLUDE(Value)

## HangFire.Hash

| Spalte | Typ | Null | Default / Computed |
|---|---|---|---|
| Id | int | NOT NULL IDENTITY |  |
| Key | nvarchar(100) | NOT NULL |  |
| Field | nvarchar(100) | NOT NULL |  |
| Value | nvarchar(max) | NULL |  |
| ExpireAt | datetime2 | NULL |  |

- PK: Id
- IX: ExpireAt INCLUDE(Id)
- IX: Key INCLUDE(ExpireAt)
- IX: UNIQUE Key,Field

## HangFire.Job

| Spalte | Typ | Null | Default / Computed |
|---|---|---|---|
| Id | int | NOT NULL IDENTITY |  |
| StateId | int | NULL |  |
| StateName | nvarchar(20) | NULL |  |
| InvocationData | nvarchar(max) | NOT NULL |  |
| Arguments | nvarchar(max) | NOT NULL |  |
| CreatedAt | datetime | NOT NULL |  |
| ExpireAt | datetime | NULL |  |

- PK: Id
- IX: ExpireAt INCLUDE(Id)
- IX: StateName

## HangFire.JobParameter

| Spalte | Typ | Null | Default / Computed |
|---|---|---|---|
| Id | int | NOT NULL IDENTITY |  |
| JobId | int | NOT NULL |  |
| Name | nvarchar(40) | NOT NULL |  |
| Value | nvarchar(max) | NULL |  |

- PK: Id
- FK: JobId -> HangFire.Job(Id)
- IX: JobId,Name

## HangFire.JobQueue

| Spalte | Typ | Null | Default / Computed |
|---|---|---|---|
| Id | int | NOT NULL IDENTITY |  |
| JobId | int | NOT NULL |  |
| Queue | nvarchar(50) | NOT NULL |  |
| FetchedAt | datetime | NULL |  |

- PK: Id
- IX: Queue,FetchedAt

## HangFire.List

| Spalte | Typ | Null | Default / Computed |
|---|---|---|---|
| Id | int | NOT NULL IDENTITY |  |
| Key | nvarchar(100) | NOT NULL |  |
| Value | nvarchar(max) | NULL |  |
| ExpireAt | datetime | NULL |  |

- PK: Id
- IX: ExpireAt INCLUDE(Id)
- IX: Key INCLUDE(ExpireAt,Value)

## HangFire.Schema

| Spalte | Typ | Null | Default / Computed |
|---|---|---|---|
| Version | int | NOT NULL |  |

- PK: Version

## HangFire.Server

| Spalte | Typ | Null | Default / Computed |
|---|---|---|---|
| Id | nvarchar(100) | NOT NULL |  |
| Data | nvarchar(max) | NULL |  |
| LastHeartbeat | datetime | NOT NULL |  |

- PK: Id

## HangFire.Set

| Spalte | Typ | Null | Default / Computed |
|---|---|---|---|
| Id | int | NOT NULL IDENTITY |  |
| Key | nvarchar(100) | NOT NULL |  |
| Score | float(53,0) | NOT NULL |  |
| Value | nvarchar(256) | NOT NULL |  |
| ExpireAt | datetime | NULL |  |

- PK: Id
- IX: ExpireAt INCLUDE(Id)
- IX: Key INCLUDE(ExpireAt,Value)
- IX: UNIQUE Key,Value

## HangFire.State

| Spalte | Typ | Null | Default / Computed |
|---|---|---|---|
| Id | int | NOT NULL IDENTITY |  |
| JobId | int | NOT NULL |  |
| Name | nvarchar(20) | NOT NULL |  |
| Reason | nvarchar(100) | NULL |  |
| CreatedAt | datetime | NOT NULL |  |
| Data | nvarchar(max) | NULL |  |

- PK: Id
- FK: JobId -> HangFire.Job(Id)
- IX: JobId

# Schema `MAM`


## MAM.ActivityLog

| Spalte | Typ | Null | Default / Computed |
|---|---|---|---|
| ActivityLogId | bigint | NOT NULL IDENTITY |  |
| ParentActivityLogId | bigint | NULL |  |
| Identifier | MAM.utPublicIdentifier=nvarchar(200) | NOT NULL | (lower(CONVERT([nvarchar](50),newid()))) |
| NodeId | bigint | NULL |  |
| ObjectType | MAM.utEnum=int | NOT NULL |  |
| ObjectIdentifier | MAM.utPublicIdentifier=nvarchar(200) | NULL |  |
| Type | MAM.utEnum=int | NULL |  |
| ChangeType | MAM.utEnum=int | NULL |  |
| UserIdentifier | MAM.utPublicIdentifier=nvarchar(200) | NULL |  |
| CreatedAt | MAM.utMutDate=datetime2 | NOT NULL |  |
| RowVersion | rowversion | NOT NULL |  |

- PK: ActivityLogId
- UNIQUE: Identifier
- FK: ParentActivityLogId -> MAM.ActivityLog(ActivityLogId)
- FK: NodeId -> MAM.Node(NodeId)
- IX: ParentActivityLogId INCLUDE(NodeId,ObjectType,ObjectIdentifier)

## MAM.ActivityLogAdditionalData

| Spalte | Typ | Null | Default / Computed |
|---|---|---|---|
| ActivityLogAdditionalDataId | bigint | NOT NULL IDENTITY |  |
| ActivityLogId | bigint | NOT NULL |  |
| Name | nvarchar(850) | NOT NULL |  |
| Value | nvarchar(max) | NULL |  |

- PK: ActivityLogAdditionalDataId
- UNIQUE: ActivityLogId, Name
- FK: ActivityLogId -> MAM.ActivityLog(ActivityLogId)

## MAM.ActivityLogUser

| Spalte | Typ | Null | Default / Computed |
|---|---|---|---|
| ActivityLogUserId | bigint | NOT NULL IDENTITY |  |
| ActivityLogId | bigint | NOT NULL |  |
| UserId | bigint | NOT NULL |  |
| ReadAt | datetime2 | NULL |  |
| RowVersion | rowversion | NOT NULL |  |

- PK: ActivityLogUserId
- UNIQUE: ActivityLogId, UserId
- FK: UserId -> Admin.User(UserId)
- FK: ActivityLogId -> MAM.ActivityLog(ActivityLogId)

## MAM.ArchiveExtractionJob

| Spalte | Typ | Null | Default / Computed |
|---|---|---|---|
| ArchiveExtractionJobId | bigint | NOT NULL IDENTITY |  |
| ArchiveAssetSetIdentifier | MAM.utPublicIdentifier=nvarchar(200) | NOT NULL |  |
| OutputCategoryIdentifier | MAM.utPublicIdentifier=nvarchar(200) | NOT NULL |  |
| WorkspaceIdentifier | MAM.utPublicIdentifier=nvarchar(200) | NULL |  |
| ArchiveExtractionMode | MAM.utEnum=int | NOT NULL |  |
| DeleteAssetAfterExtraction | bit | NOT NULL |  |
| Status | MAM.utEnum=int | NOT NULL |  |
| BackgroundJobId | bigint | NOT NULL |  |
| CreatedAt | MAM.utDateTime=datetime2 | NOT NULL |  |
| CreatedBy | MAM.utMutUser=nvarchar(200) | NOT NULL |  |
| InitiatedAt | MAM.utDateTime=datetime2 | NULL |  |
| FinishedAt | MAM.utDateTime=datetime2 | NULL |  |
| MutDate | MAM.utMutDate=datetime2 | NULL |  |
| RowVersion | rowversion | NOT NULL |  |

- PK: ArchiveExtractionJobId
- FK: BackgroundJobId -> MAM.BackgroundJob(BackgroundJobId)
- IX: BackgroundJobId,WorkspaceIdentifier

## MAM.ArchiveExtractionJobEntry

| Spalte | Typ | Null | Default / Computed |
|---|---|---|---|
| ArchiveExtractionJobEntryId | bigint | NOT NULL IDENTITY |  |
| ArchiveExtractionJobId | bigint | NOT NULL |  |
| EntryType | MAM.utEnum=int | NOT NULL |  |
| Status | MAM.utEnum=int | NOT NULL |  |
| Path | nvarchar(max) | NOT NULL |  |
| Name | MAM.utFilename=nvarchar(850) | NOT NULL |  |
| CreatedEntityIdentifier | MAM.utPublicIdentifier=nvarchar(200) | NULL |  |
| CreatedEntityTypeIdentifier | MAM.utPublicIdentifier=nvarchar(200) | NULL |  |

- PK: ArchiveExtractionJobEntryId
- FK: ArchiveExtractionJobId -> MAM.ArchiveExtractionJob(ArchiveExtractionJobId)

## MAM.AssetDerivation

| Spalte | Typ | Null | Default / Computed |
|---|---|---|---|
| AssetDerivationId | bigint | NOT NULL IDENTITY |  |
| Identifier | MAM.utPublicIdentifier=nvarchar(200) | NOT NULL | (lower(CONVERT([nvarchar](50),newid()))) |
| AssetVersionId | bigint | NOT NULL |  |
| DerivationTypeId | bigint | NOT NULL |  |
| DataId | MAM.utPublicIdentifier=nvarchar(200) | NULL |  |
| FileTypeId | bigint | NOT NULL |  |
| FileSize | bigint | NOT NULL |  |
| FileHash | nvarchar(850) | NULL |  |
| MutUser | MAM.utMutUser=nvarchar(200) | NULL |  |
| MutDate | MAM.utMutDate=datetime2 | NULL |  |
| RowVersion | rowversion | NOT NULL |  |
| Status | MAM.utEnum=int | NOT NULL | ((0)) |
| DerivationFeedback | nvarchar(1024) | NULL |  |

- PK: AssetDerivationId
- UNIQUE: Identifier
- FK: AssetVersionId -> MAM.AssetVersion(AssetVersionId)
- FK: FileTypeId -> MAM.FileType(FileTypeId)
- FK: DerivationTypeId -> MAM.DerivationType(DerivationTypeId)
- IX: AssetVersionId,DerivationTypeId INCLUDE(Identifier)
- IX: DerivationTypeId,AssetDerivationId,AssetVersionId INCLUDE(Identifier,DataId,FileHash)
- IX: DerivationTypeId INCLUDE(AssetVersionId,FileSize)
- IX: Status INCLUDE(AssetVersionId,DerivationTypeId)
- IX: AssetVersionId
- IX: UNIQUE AssetVersionId,DerivationTypeId

## MAM.AssetDerivationJob

| Spalte | Typ | Null | Default / Computed |
|---|---|---|---|
| AssetDerivationJobId | bigint | NOT NULL IDENTITY |  |
| Identifier | MAM.utPublicIdentifier=nvarchar(200) | NOT NULL | (lower(CONVERT([nvarchar](50),newid()))) |
| BackgroundJobId | bigint | NOT NULL |  |
| Status | MAM.utEnum=int | NOT NULL |  |
| MutUser | MAM.utMutUser=nvarchar(200) | NULL |  |
| MutDate | MAM.utMutDate=datetime2 | NULL |  |
| RowVersion | rowversion | NOT NULL |  |
| CreatedAt | datetime2 | NOT NULL | (getutcdate()) |
| CreatedBy | MAM.utMutUser=nvarchar(200) | NOT NULL |  |
| FinishedAt | datetime2 | NULL |  |
| InitiatedAt | datetime2 | NULL |  |

- PK: AssetDerivationJobId
- FK: BackgroundJobId -> MAM.BackgroundJob(BackgroundJobId)

## MAM.AssetDerivationJobReport

| Spalte | Typ | Null | Default / Computed |
|---|---|---|---|
| AssetDerivationJobReportId | bigint | NOT NULL IDENTITY |  |
| AssetDerivationJobId | bigint | NOT NULL |  |
| AssetSetIdentifier | MAM.utPublicIdentifier=nvarchar(200) | NOT NULL |  |
| AssetVersionIdentifier | MAM.utPublicIdentifier=nvarchar(200) | NOT NULL |  |
| AssetDerivationIdentifier | MAM.utPublicIdentifier=nvarchar(200) | NOT NULL |  |
| AssetTypeId | bigint | NOT NULL |  |
| DerivationTypeId | bigint | NOT NULL |  |
| FileTypeId | bigint | NOT NULL |  |
| FileSize | bigint | NOT NULL |  |
| FileName | MAM.utFilename=nvarchar(850) | NOT NULL |  |
| Status | MAM.utEnum=int | NOT NULL | ((0)) |

- PK: AssetDerivationJobReportId
- FK: AssetDerivationJobId -> MAM.AssetDerivationJob(AssetDerivationJobId)
- FK: AssetTypeId -> MAM.AssetType(AssetTypeId)
- FK: DerivationTypeId -> MAM.DerivationType(DerivationTypeId)
- FK: FileTypeId -> MAM.FileType(FileTypeId)

## MAM.AssetSet

| Spalte | Typ | Null | Default / Computed |
|---|---|---|---|
| AssetSetId | bigint | NOT NULL IDENTITY |  |
| AssetSetNodeId | bigint | NOT NULL |  |
| Created | MAM.utDateTime=datetime2 | NOT NULL |  |
| AssetSetStatus | MAM.utEnum=int | NOT NULL |  |
| MutUser | MAM.utMutUser=nvarchar(200) | NULL |  |
| MutDate | MAM.utMutDate=datetime2 | NULL |  |
| RowVersion | rowversion | NOT NULL |  |

- PK: AssetSetId
- FK: AssetSetNodeId -> MAM.Node(NodeId)
- IX: AssetSetNodeId

## MAM.AssetSetDuplicate

| Spalte | Typ | Null | Default / Computed |
|---|---|---|---|
| AssetSetDuplicateId | bigint | NOT NULL IDENTITY |  |
| AssetSetIdentifier | MAM.utPublicIdentifier=nvarchar(200) | NOT NULL |  |
| CreatedAt | datetime2 | NOT NULL |  |
| FileName | MAM.utFilename=nvarchar(850) | NOT NULL |  |
| ImportedBy | MAM.utMutUser=nvarchar(200) | NOT NULL |  |
| DuplicateAssetSetIdentifier | MAM.utPublicIdentifier=nvarchar(200) | NOT NULL |  |
| DuplicateCreatedAt | datetime2 | NOT NULL |  |
| DuplicateFileName | MAM.utFilename=nvarchar(850) | NOT NULL |  |
| DuplicateImportedBy | MAM.utMutUser=nvarchar(200) | NOT NULL |  |
| MutUser | MAM.utMutUser=nvarchar(200) | NULL |  |
| MutDate | MAM.utMutDate=datetime2 | NULL |  |
| RowVersion | rowversion | NOT NULL |  |

- PK: AssetSetDuplicateId

## MAM.AssetType

| Spalte | Typ | Null | Default / Computed |
|---|---|---|---|
| AssetTypeId | bigint | NOT NULL IDENTITY |  |
| Identifier | MAM.utPublicIdentifier=nvarchar(200) | NOT NULL | (lower(CONVERT([nvarchar](50),newid()))) |
| DownloadOriginalAlways | bit | NULL |  |
| MutUser | MAM.utMutUser=nvarchar(200) | NULL |  |
| MutDate | MAM.utMutDate=datetime2 | NULL |  |
| RowVersion | rowversion | NOT NULL |  |
| PropertyListValueIdentifier | nvarchar(850) | NOT NULL | ('') |

- PK: AssetTypeId
- UNIQUE: PropertyListValueIdentifier
- UNIQUE: Identifier

## MAM.AssetTypeDerivationType

| Spalte | Typ | Null | Default / Computed |
|---|---|---|---|
| AssetTypeDerivationTypeId | bigint | NOT NULL IDENTITY |  |
| AssetTypeId | bigint | NOT NULL |  |
| DerivationTypeId | bigint | NOT NULL |  |
| IsDefaultDerivation | bit | NOT NULL | ((0)) |
| AllowManualUpload | bit | NOT NULL | ((0)) |
| IsOnDemand | bit | NOT NULL | ((0)) |
| ShowInDownloads | bit | NOT NULL | ((0)) |
| UseAsThumbnail | bit | NOT NULL | ((0)) |
| UseAsPreview | bit | NOT NULL | ((0)) |
| MutUser | MAM.utMutUser=nvarchar(200) | NULL |  |
| MutDate | MAM.utMutDate=datetime2 | NULL |  |
| RowVersion | rowversion | NOT NULL |  |

- PK: AssetTypeDerivationTypeId
- UNIQUE: AssetTypeId, DerivationTypeId
- FK: AssetTypeId -> MAM.AssetType(AssetTypeId)
- FK: DerivationTypeId -> MAM.DerivationType(DerivationTypeId)

## MAM.AssetTypeProperty

| Spalte | Typ | Null | Default / Computed |
|---|---|---|---|
| AssetTypePropertyId | bigint | NOT NULL IDENTITY |  |
| Identifier | MAM.utPublicIdentifier=nvarchar(200) | NOT NULL | (lower(CONVERT([nvarchar](50),newid()))) |
| AssetTypeId | bigint | NOT NULL |  |
| PropertyId | bigint | NOT NULL |  |
| MutUser | MAM.utMutUser=nvarchar(200) | NULL |  |
| MutDate | MAM.utMutDate=datetime2 | NULL |  |
| RowVersion | rowversion | NOT NULL |  |

- PK: AssetTypePropertyId
- UNIQUE: Identifier
- FK: AssetTypeId -> MAM.AssetType(AssetTypeId)
- FK: PropertyId -> MAM.Property(PropertyId)
- IX: AssetTypeId
- IX: PropertyId

## MAM.AssetVersion

| Spalte | Typ | Null | Default / Computed |
|---|---|---|---|
| AssetVersionId | bigint | NOT NULL IDENTITY |  |
| AssetVersionNodeId | bigint | NOT NULL |  |
| Created | MAM.utDateTime=datetime2 | NOT NULL |  |
| AssetVersionStatus | MAM.utEnum=int | NOT NULL |  |
| ProcessingStatus | MAM.utEnum=int | NOT NULL |  |
| OriginalFilename | MAM.utFilename=nvarchar(850) | NULL |  |
| OriginalName | MAM.utFilename=nvarchar(850) | NULL |  |
| OriginalFileEnding | MAM.utFilename=nvarchar(850) | NULL |  |
| Version | MAM.utNumber=int | NOT NULL |  |
| MutUser | MAM.utMutUser=nvarchar(200) | NULL |  |
| MutDate | MAM.utMutDate=datetime2 | NULL |  |
| RowVersion | rowversion | NOT NULL |  |
| AssetTypeId | bigint | NOT NULL | ((0)) |

- PK: AssetVersionId
- FK: AssetTypeId -> MAM.AssetType(AssetTypeId)
- FK: AssetVersionNodeId -> MAM.Node(NodeId)
- IX: AssetVersionId,AssetVersionNodeId INCLUDE(OriginalName)
- IX: AssetVersionNodeId INCLUDE(Created,AssetVersionStatus,ProcessingStatus,OriginalFilename,OriginalName,OriginalFileEnding,Version,AssetTypeId)

## MAM.BackgroundJob

| Spalte | Typ | Null | Default / Computed |
|---|---|---|---|
| BackgroundJobId | bigint | NOT NULL IDENTITY |  |
| Identifier | MAM.utPublicIdentifier=nvarchar(200) | NOT NULL | (lower(CONVERT([nvarchar](50),newid()))) |
| Type | MAM.utEnum=int | NOT NULL | ((0)) |
| HangfireJobId | nvarchar(21) | NULL |  |
| MutUser | MAM.utMutUser=nvarchar(200) | NULL |  |
| MutDate | MAM.utMutDate=datetime2 | NULL |  |
| RowVersion | rowversion | NOT NULL |  |

- PK: BackgroundJobId
- UNIQUE: Identifier

## MAM.BackgroundJobTrigger

| Spalte | Typ | Null | Default / Computed |
|---|---|---|---|
| BackgroundJobTriggerId | bigint | NOT NULL IDENTITY |  |
| TransactionIdentifier | MAM.utPublicIdentifier=nvarchar(200) | NOT NULL |  |
| JobType | MAM.utEnum=int | NOT NULL |  |
| CreatedAt | MAM.utDateTime=datetime2 | NOT NULL |  |
| EntityIdentifier | MAM.utPublicIdentifier=nvarchar(200) | NULL |  |
| EntityTypeIdentifier | MAM.utPublicIdentifier=nvarchar(200) | NULL |  |
| Done | bit | NOT NULL |  |
| MutUser | MAM.utMutUser=nvarchar(200) | NULL |  |
| MutDate | MAM.utMutDate=datetime2 | NULL |  |
| RowVersion | rowversion | NOT NULL |  |
| PermissionIdentifier | MAM.utPublicIdentifier=nvarchar(200) | NULL |  |

- PK: BackgroundJobTriggerId

## MAM.BillingContact

| Spalte | Typ | Null | Default / Computed |
|---|---|---|---|
| BillingContactId | bigint | NOT NULL IDENTITY |  |
| NodeId | bigint | NOT NULL |  |
| Name | nvarchar(200) | NOT NULL |  |
| EmailAddress | nvarchar(200) | NOT NULL |  |
| Status | MAM.utEnum=int | NOT NULL |  |
| CreatedAt | datetime2 | NOT NULL |  |
| CreatedBy | nvarchar(200) | NOT NULL |  |
| UpdatedAt | datetime2 | NOT NULL |  |
| UpdatedBy | nvarchar(200) | NOT NULL |  |
| MutUser | MAM.utMutUser=nvarchar(200) | NULL |  |
| MutDate | MAM.utMutDate=datetime2 | NULL |  |
| RowVersion | rowversion | NOT NULL |  |

- PK: BillingContactId
- FK: NodeId -> MAM.Node(NodeId)

## MAM.Buyout

| Spalte | Typ | Null | Default / Computed |
|---|---|---|---|
| BuyoutId | bigint | NOT NULL IDENTITY |  |
| Identifier | MAM.utPublicIdentifier=nvarchar(200) | NOT NULL |  |
| RightsOwnerId | bigint | NOT NULL |  |
| PriceListId | bigint | NOT NULL |  |
| LicenseId | bigint | NOT NULL |  |
| CurrencyId | bigint | NOT NULL |  |
| Description | nvarchar(max) | NULL |  |
| Basis | decimal(18,0) | NOT NULL |  |
| AdditionalFeePercentage | decimal(18,0) | NOT NULL |  |
| FromDate | datetime2 | NOT NULL |  |
| ToDate | datetime2 | NOT NULL |  |
| CreatedAt | datetime2 | NOT NULL |  |
| CreatedBy | nvarchar(200) | NOT NULL |  |
| UpdatedAt | datetime2 | NOT NULL |  |
| UpdatedBy | nvarchar(200) | NOT NULL |  |
| RowVersion | rowversion | NOT NULL |  |

- PK: BuyoutId
- UNIQUE: Identifier
- FK: CurrencyId -> MAM.Currency(CurrencyId)
- FK: LicenseId -> MAM.License(LicenseId)
- FK: PriceListId -> MAM.PriceList(PriceListId)
- FK: RightsOwnerId -> MAM.RightsOwner(RightsOwnerId)

## MAM.Category

| Spalte | Typ | Null | Default / Computed |
|---|---|---|---|
| CategoryId | bigint | NOT NULL IDENTITY |  |
| CategoryNodeId | bigint | NOT NULL |  |
| TranslationIdentifier | MAM.utTranslationIdentifier=nvarchar(80) | NOT NULL |  |
| MutUser | MAM.utMutUser=nvarchar(200) | NULL |  |
| MutDate | MAM.utMutDate=datetime2 | NULL |  |
| RowVersion | rowversion | NOT NULL |  |
| SequenceNr | MAM.utSequenceNr=int | NOT NULL |  |

- PK: CategoryId
- FK: CategoryNodeId -> MAM.Node(NodeId)
- IX: CategoryNodeId INCLUDE(CategoryId,TranslationIdentifier)

## MAM.Comment

| Spalte | Typ | Null | Default / Computed |
|---|---|---|---|
| CommentId | bigint | NOT NULL IDENTITY |  |
| NodeId | bigint | NOT NULL |  |
| Text | nvarchar(max) | NOT NULL |  |
| CreatedBy | MAM.utMutUser=nvarchar(200) | NOT NULL |  |
| CreatedAt | MAM.utMutDate=datetime2 | NOT NULL |  |
| MutUser | MAM.utMutUser=nvarchar(200) | NULL |  |
| MutDate | MAM.utMutDate=datetime2 | NULL |  |
| RowVersion | rowversion | NOT NULL |  |

- PK: CommentId
- FK: NodeId -> MAM.Node(NodeId)

## MAM.Currency

| Spalte | Typ | Null | Default / Computed |
|---|---|---|---|
| CurrencyId | bigint | NOT NULL IDENTITY |  |
| Identifier | MAM.utPublicIdentifier=nvarchar(200) | NOT NULL |  |
| ShortName | nvarchar(200) | NOT NULL |  |
| CreatedAt | datetime2 | NOT NULL |  |
| CreatedBy | nvarchar(200) | NOT NULL |  |
| UpdatedAt | datetime2 | NOT NULL |  |
| UpdatedBy | nvarchar(200) | NOT NULL |  |
| RowVersion | rowversion | NOT NULL |  |

- PK: CurrencyId
- UNIQUE: Identifier

## MAM.CustomNode

| Spalte | Typ | Null | Default / Computed |
|---|---|---|---|
| CustomNodeId | bigint | NOT NULL IDENTITY |  |
| NodeId | bigint | NOT NULL |  |
| TranslationIdentifier | MAM.utTranslationIdentifier=nvarchar(80) | NOT NULL |  |
| MutUser | MAM.utMutUser=nvarchar(200) | NULL |  |
| MutDate | MAM.utMutDate=datetime2 | NULL |  |
| RowVersion | rowversion | NOT NULL |  |

- PK: CustomNodeId
- FK: NodeId -> MAM.Node(NodeId)

## MAM.DbStatus

| Spalte | Typ | Null | Default / Computed |
|---|---|---|---|
| DbStatusId | bigint | NOT NULL IDENTITY |  |
| Identifier | MAM.utPublicIdentifier=nvarchar(200) | NOT NULL | (lower(CONVERT([nvarchar](50),newid()))) |
| Description | MAM.utDescription=nvarchar(1024) | NOT NULL |  |
| ScriptAutor | nvarchar(40) | NOT NULL |  |
| ScriptCreateDate | MAM.utDate=date | NOT NULL |  |
| AppVersion | nvarchar(40) | NOT NULL |  |
| ScriptRunDate | MAM.utDate=date | NOT NULL |  |
| RowVersion | rowversion | NOT NULL |  |

- PK: DbStatusId
- UNIQUE: Identifier

## MAM.DerivationJobDefinition

| Spalte | Typ | Null | Default / Computed |
|---|---|---|---|
| DerivationJobDefinitionId | bigint | NOT NULL IDENTITY |  |
| Identifier | MAM.utPublicIdentifier=nvarchar(200) | NOT NULL | (lower(CONVERT([nvarchar](50),newid()))) |
| InputDerivationDataType | int | NOT NULL |  |
| JobDefinition | nvarchar(max) | NOT NULL |  |
| OutputDerivationDataType | int | NOT NULL |  |
| DerivationTypeId | bigint | NOT NULL |  |
| OutputFileTypeId | bigint | NOT NULL |  |
| MutUser | MAM.utMutUser=nvarchar(200) | NULL |  |
| MutDate | MAM.utMutDate=datetime2 | NULL |  |
| RowVersion | rowversion | NOT NULL |  |
| IsSystem | bit | NOT NULL | ((0)) |

- PK: DerivationJobDefinitionId
- UNIQUE: Identifier
- FK: DerivationTypeId -> MAM.DerivationType(DerivationTypeId)
- FK: OutputFileTypeId -> MAM.FileType(FileTypeId)

## MAM.DerivationJobDefinitionInputFileType

| Spalte | Typ | Null | Default / Computed |
|---|---|---|---|
| DerivationJobDefinitionInputFileTypeId | bigint | NOT NULL IDENTITY |  |
| Identifier | MAM.utPublicIdentifier=nvarchar(200) | NOT NULL | (lower(CONVERT([nvarchar](50),newid()))) |
| DerivationJobDefinitionId | bigint | NOT NULL |  |
| FileTypeId | bigint | NOT NULL |  |
| MutUser | MAM.utMutUser=nvarchar(200) | NULL |  |
| MutDate | MAM.utMutDate=datetime2 | NULL |  |
| RowVersion | rowversion | NOT NULL |  |

- PK: DerivationJobDefinitionInputFileTypeId
- FK: DerivationJobDefinitionId -> MAM.DerivationJobDefinition(DerivationJobDefinitionId)
- FK: FileTypeId -> MAM.FileType(FileTypeId)

## MAM.DerivationType

| Spalte | Typ | Null | Default / Computed |
|---|---|---|---|
| DerivationTypeId | bigint | NOT NULL IDENTITY |  |
| Identifier | MAM.utPublicIdentifier=nvarchar(200) | NOT NULL | (lower(CONVERT([nvarchar](50),newid()))) |
| MutUser | MAM.utMutUser=nvarchar(200) | NULL |  |
| MutDate | MAM.utMutDate=datetime2 | NULL |  |
| RowVersion | rowversion | NOT NULL |  |
| TranslationIdentifier | MAM.utPublicIdentifier=nvarchar(200) | NOT NULL |  |
| IsSystem | bit | NOT NULL | ((0)) |
| IsDeleted | bit | NOT NULL | ((0)) |

- PK: DerivationTypeId
- UNIQUE: TranslationIdentifier
- IX: DerivationTypeId INCLUDE(Identifier,MutUser,MutDate,RowVersion)
- IX: UNIQUE Identifier INCLUDE(IsDeleted) WHERE ([IsDeleted]<>(1))

## MAM.Edge

| Spalte | Typ | Null | Default / Computed |
|---|---|---|---|
| EdgeId | bigint | NOT NULL IDENTITY |  |
| Identifier | MAM.utPublicIdentifier=nvarchar(200) | NOT NULL | (lower(CONVERT([nvarchar](50),newid()))) |
| FromNodeId | bigint | NOT NULL |  |
| ToNodeId | bigint | NOT NULL |  |
| EdgeTypeId | bigint | NOT NULL |  |
| MutUser | MAM.utMutUser=nvarchar(200) | NULL |  |
| MutDate | MAM.utMutDate=datetime2 | NULL |  |
| RowVersion | rowversion | NOT NULL |  |

- PK: EdgeId
- UNIQUE: Identifier
- FK: EdgeTypeId -> MAM.EdgeType(EdgeTypeId)
- FK: FromNodeId -> MAM.Node(NodeId)
- FK: ToNodeId -> MAM.Node(NodeId)
- IX: FromNodeId,ToNodeId,EdgeTypeId INCLUDE(EdgeId)
- IX: FromNodeId,EdgeTypeId INCLUDE(EdgeId,ToNodeId,Identifier)
- IX: FromNodeId,ToNodeId,EdgeTypeId INCLUDE(EdgeId)
- IX: ToNodeId,EdgeTypeId INCLUDE(EdgeId,FromNodeId,Identifier)

## MAM.EdgeType

| Spalte | Typ | Null | Default / Computed |
|---|---|---|---|
| EdgeTypeId | bigint | NOT NULL IDENTITY |  |
| Identifier | MAM.utPublicIdentifier=nvarchar(200) | NOT NULL | (lower(CONVERT([nvarchar](50),newid()))) |
| EdgeSemantic | MAM.utEnum=int | NOT NULL |  |
| TranslationIdentifier | MAM.utTranslationIdentifier=nvarchar(80) | NOT NULL |  |
| Status | MAM.utEnum=int | NOT NULL |  |
| IsDirected | bit | NOT NULL |  |
| IsSystem | bit | NOT NULL |  |
| IsDeleted | bit | NOT NULL |  |
| MutUser | MAM.utMutUser=nvarchar(200) | NULL |  |
| MutDate | MAM.utMutDate=datetime2 | NULL |  |
| RowVersion | rowversion | NOT NULL |  |
| IsUsedForAssetSearch | bit | NOT NULL | ((0)) |

- PK: EdgeTypeId
- IX: UNIQUE Identifier INCLUDE(EdgeSemantic,TranslationIdentifier,Status,IsDirected,IsSystem,IsDeleted) WHERE ([IsDeleted]<>(1))

## MAM.EdgeTypeAllowedNodeTypes

| Spalte | Typ | Null | Default / Computed |
|---|---|---|---|
| EdgeTypeAllowedNodeTypeId | bigint | NOT NULL IDENTITY |  |
| EdgeTypeId | bigint | NOT NULL |  |
| FromNodeTypeId | bigint | NOT NULL |  |
| ToNodeTypeId | bigint | NOT NULL |  |
| MutUser | MAM.utMutUser=nvarchar(200) | NULL |  |
| MutDate | MAM.utMutDate=datetime2 | NULL |  |
| RowVersion | rowversion | NOT NULL |  |

- PK: EdgeTypeAllowedNodeTypeId
- FK: EdgeTypeId -> MAM.EdgeType(EdgeTypeId)
- FK: FromNodeTypeId -> MAM.NodeType(NodeTypeId)
- FK: ToNodeTypeId -> MAM.NodeType(NodeTypeId)

## MAM.FileNamingDefinition

| Spalte | Typ | Null | Default / Computed |
|---|---|---|---|
| FileNamingDefinitionId | bigint | NOT NULL IDENTITY |  |
| Identifier | MAM.utPublicIdentifier=nvarchar(200) | NOT NULL | (lower(CONVERT([nvarchar],newid()))) |
| TranslationIdentifier | MAM.utTranslationIdentifier=nvarchar(80) | NOT NULL |  |
| Format | nvarchar(2048) | NOT NULL |  |
| CharacterCasing | MAM.utEnum=int | NOT NULL |  |
| ReplaceIllegalCharacters | bit | NOT NULL | ((1)) |
| AbsentPropertyStrategy | MAM.utEnum=int | NOT NULL |  |
| CreatedBy | MAM.utMutUser=nvarchar(200) | NOT NULL |  |
| MutUser | MAM.utMutUser=nvarchar(200) | NULL |  |
| MutDate | MAM.utMutDate=datetime2 | NULL |  |
| RowVersion | rowversion | NOT NULL |  |

- PK: FileNamingDefinitionId
- UNIQUE: Identifier

## MAM.FileNamingDefinitionProperty

| Spalte | Typ | Null | Default / Computed |
|---|---|---|---|
| FileNamingDefinitionPropertyId | bigint | NOT NULL IDENTITY |  |
| FileNamingDefinitionId | bigint | NOT NULL |  |
| PropertyDefinitionId | bigint | NOT NULL |  |

- PK: FileNamingDefinitionPropertyId
- FK: FileNamingDefinitionId -> MAM.FileNamingDefinition(FileNamingDefinitionId)
- FK: PropertyDefinitionId -> MAM.PropertyDefinition(PropertyDefinitionId)

## MAM.FileNamingDefinitionTenant

| Spalte | Typ | Null | Default / Computed |
|---|---|---|---|
| FileNamingDefinitionTenantId | bigint | NOT NULL IDENTITY |  |
| FileNamingDefinitionId | bigint | NOT NULL |  |
| TenantId | bigint | NULL |  |
| IsDefault | bit | NOT NULL |  |

- PK: FileNamingDefinitionTenantId
- UNIQUE: FileNamingDefinitionId, TenantId
- FK: FileNamingDefinitionId -> MAM.FileNamingDefinition(FileNamingDefinitionId)
- FK: TenantId -> Admin.Tenant(TenantId)
- IX: UNIQUE TenantId WHERE ([IsDefault]=(1))

## MAM.FileReference

| Spalte | Typ | Null | Default / Computed |
|---|---|---|---|
| FileReferenceId | bigint | NOT NULL IDENTITY |  |
| Identifier | MAM.utPublicIdentifier=nvarchar(200) | NOT NULL | (lower(CONVERT([nvarchar](50),newid()))) |
| DataIdentifier | MAM.utPublicIdentifier=nvarchar(200) | NOT NULL |  |
| FileName | MAM.utFilename=nvarchar(850) | NOT NULL |  |
| FileUsageType | MAM.utEnum=int | NOT NULL |  |
| MutUser | MAM.utMutUser=nvarchar(200) | NULL |  |
| MutDate | MAM.utMutDate=datetime2 | NULL |  |
| RowVersion | rowversion | NOT NULL |  |

- PK: FileReferenceId

## MAM.FileType

| Spalte | Typ | Null | Default / Computed |
|---|---|---|---|
| FileTypeId | bigint | NOT NULL IDENTITY |  |
| Identifier | MAM.utPublicIdentifier=nvarchar(200) | NOT NULL | (lower(CONVERT([nvarchar](50),newid()))) |
| AssetTypeId | bigint | NOT NULL |  |
| FileEnding | MAM.utFilename=nvarchar(850) | NOT NULL |  |
| MimeType | MAM.utName=nvarchar(850) | NOT NULL |  |
| QualityChecksActive | bit | NOT NULL |  |
| MutUser | MAM.utMutUser=nvarchar(200) | NULL |  |
| MutDate | MAM.utMutDate=datetime2 | NULL |  |
| RowVersion | rowversion | NOT NULL |  |
| EnableBinaryDocumentExtraction | bit | NOT NULL | ((0)) |

- PK: FileTypeId
- UNIQUE: Identifier
- FK: AssetTypeId -> MAM.AssetType(AssetTypeId)
- IX: AssetTypeId
- IX: FileTypeId,AssetTypeId INCLUDE(Identifier,FileEnding,MimeType)

## MAM.FileTypeToDerivationType

| Spalte | Typ | Null | Default / Computed |
|---|---|---|---|
| FileTypeToDerivationTypeId | bigint | NOT NULL |  |
| Identifier | MAM.utPublicIdentifier=nvarchar(200) | NOT NULL | (lower(CONVERT([nvarchar](50),newid()))) |
| FileTypeId | bigint | NOT NULL |  |
| DerivationTypeId | bigint | NOT NULL |  |
| MutUser | MAM.utMutUser=nvarchar(200) | NULL |  |
| MutDate | MAM.utMutDate=datetime2 | NULL |  |
| RowVersion | rowversion | NOT NULL |  |

- PK: FileTypeToDerivationTypeId
- FK: DerivationTypeId -> MAM.DerivationType(DerivationTypeId)
- FK: FileTypeId -> MAM.FileType(FileTypeId)

## MAM.Language

| Spalte | Typ | Null | Default / Computed |
|---|---|---|---|
| LanguageId | bigint | NOT NULL IDENTITY |  |
| Identifier | MAM.utPublicIdentifier=nvarchar(200) | NOT NULL | (lower(CONVERT([nvarchar](50),newid()))) |
| Status | MAM.utEnum=int | NOT NULL |  |
| IsDefaultLanguage | bit | NOT NULL |  |
| TranslationIdentifier | MAM.utTranslationIdentifier=nvarchar(80) | NOT NULL |  |
| IsDeleted | bit | NOT NULL |  |
| MutUser | MAM.utMutUser=nvarchar(200) | NULL |  |
| MutDate | MAM.utMutDate=datetime2 | NULL |  |
| RowVersion | rowversion | NOT NULL |  |

- PK: LanguageId
- IX: UNIQUE Identifier INCLUDE(IsDefaultLanguage,TranslationIdentifier,Status,IsDeleted) WHERE ([IsDeleted]<>(1))

## MAM.License

| Spalte | Typ | Null | Default / Computed |
|---|---|---|---|
| LicenseId | bigint | NOT NULL IDENTITY |  |
| NodeId | bigint | NOT NULL |  |
| TranslationIdentifier | MAM.utTranslationIdentifier=nvarchar(80) | NOT NULL |  |
| Status | MAM.utEnum=int | NOT NULL |  |
| CreatedAt | datetime2 | NOT NULL |  |
| CreatedBy | nvarchar(200) | NOT NULL |  |
| UpdatedAt | datetime2 | NOT NULL |  |
| UpdatedBy | nvarchar(200) | NOT NULL |  |
| MutUser | MAM.utMutUser=nvarchar(200) | NULL |  |
| MutDate | MAM.utMutDate=datetime2 | NULL |  |
| RowVersion | rowversion | NOT NULL |  |
| PeriodStart | datetime2 | NOT NULL |  |
| PeriodEnd | datetime2 | NOT NULL |  |

- PK: LicenseId
- FK: NodeId -> MAM.Node(NodeId)

## MAM.Node

| Spalte | Typ | Null | Default / Computed |
|---|---|---|---|
| NodeId | bigint | NOT NULL IDENTITY |  |
| Identifier | MAM.utPublicIdentifier=nvarchar(200) | NOT NULL | (lower(CONVERT([nvarchar](50),newid()))) |
| NodeTypeId | bigint | NOT NULL |  |
| SequenceNr | MAM.utSequenceNr=int | NOT NULL |  |
| TenantId | bigint | NOT NULL |  |
| IsDeleted | bit | NOT NULL |  |
| CreateUser | MAM.utMutUser=nvarchar(200) | NOT NULL |  |
| CreateDate | MAM.utMutDate=datetime2 | NOT NULL |  |
| MutUser | MAM.utMutUser=nvarchar(200) | NULL |  |
| MutDate | MAM.utMutDate=datetime2 | NULL |  |
| RowVersion | rowversion | NOT NULL |  |
| InterruptInheritance | bit | NOT NULL | ((0)) |

- PK: NodeId
- FK: TenantId -> Admin.Tenant(TenantId)
- FK: NodeTypeId -> MAM.NodeType(NodeTypeId)
- IX: Identifier,NodeTypeId INCLUDE(NodeId)
- IX: NodeId,NodeTypeId,IsDeleted INCLUDE(Identifier,TenantId) WHERE ([IsDeleted]=(0))
- IX: TenantId INCLUDE(IsDeleted) WHERE ([IsDeleted]<>(1))
- IX: UNIQUE Identifier,NodeTypeId INCLUDE(IsDeleted,SequenceNr,TenantId) WHERE ([IsDeleted]<>(1))

## MAM.NodeActivityLogSettings

| Spalte | Typ | Null | Default / Computed |
|---|---|---|---|
| NodeActivityLogSettingsId | bigint | NOT NULL IDENTITY |  |
| NodeId | bigint | NOT NULL |  |
| UserId | bigint | NOT NULL |  |
| SendToInbox | bit | NOT NULL |  |
| SendEmail | bit | NOT NULL |  |
| RowVersion | rowversion | NOT NULL |  |

- PK: NodeActivityLogSettingsId
- UNIQUE: NodeId, UserId
- FK: NodeId -> MAM.Node(NodeId)
- FK: UserId -> Admin.User(UserId)

## MAM.NodeType

| Spalte | Typ | Null | Default / Computed |
|---|---|---|---|
| NodeTypeId | bigint | NOT NULL IDENTITY |  |
| Identifier | MAM.utPublicIdentifier=nvarchar(200) | NOT NULL | (lower(CONVERT([nvarchar](50),newid()))) |
| NodeSemantic | MAM.utEnum=int | NOT NULL |  |
| MutUser | MAM.utMutUser=nvarchar(200) | NULL |  |
| MutDate | MAM.utMutDate=datetime2 | NULL |  |
| RowVersion | rowversion | NOT NULL |  |
| TranslationIdentifier | MAM.utPublicIdentifier=nvarchar(200) | NULL |  |
| Status | MAM.utEnum=int | NOT NULL | ((1)) |
| IsSystem | bit | NOT NULL | ((0)) |
| IsDeleted | bit | NOT NULL | ((0)) |

- PK: NodeTypeId
- IX: UNIQUE Identifier INCLUDE(IsDeleted) WHERE ([IsDeleted]<>(1))

## MAM.OutgoingEmail

| Spalte | Typ | Null | Default / Computed |
|---|---|---|---|
| OutgoingEmailId | bigint | NOT NULL IDENTITY |  |
| TemplateIdentifier | MAM.utPublicIdentifier=nvarchar(200) | NOT NULL |  |
| CreatedAt | MAM.utDateTime=datetime2 | NOT NULL | (getutcdate()) |
| MutUser | MAM.utMutUser=nvarchar(200) | NULL |  |
| MutDate | MAM.utMutDate=datetime2 | NULL |  |

- PK: OutgoingEmailId

## MAM.OutgoingEmailRecipientAddress

| Spalte | Typ | Null | Default / Computed |
|---|---|---|---|
| OutgoingEmailRecipientAddressId | bigint | NOT NULL IDENTITY |  |
| OutgoingEmailId | bigint | NOT NULL |  |
| EmailAddress | nvarchar(320) | NOT NULL |  |
- FK: OutgoingEmailId -> MAM.OutgoingEmail(OutgoingEmailId)

## MAM.OutgoingEmailRecipientUser

| Spalte | Typ | Null | Default / Computed |
|---|---|---|---|
| OutgoingEmailRecipientUserId | bigint | NOT NULL IDENTITY |  |
| OutgoingEmailId | bigint | NOT NULL |  |
| UserIdentifier | MAM.utPublicIdentifier=nvarchar(200) | NOT NULL |  |
- FK: OutgoingEmailId -> MAM.OutgoingEmail(OutgoingEmailId)

## MAM.OutgoingEmailTemplateParameter

| Spalte | Typ | Null | Default / Computed |
|---|---|---|---|
| OutgoingEmailTemplateParameterId | bigint | NOT NULL IDENTITY |  |
| OutgoingEmailId | bigint | NOT NULL |  |
| Name | nvarchar(320) | NOT NULL |  |
| Value | nvarchar(1536) | NULL |  |
- UNIQUE: OutgoingEmailId, Name
- FK: OutgoingEmailId -> MAM.OutgoingEmail(OutgoingEmailId)

## MAM.OutgoingMessageQueueEvent

| Spalte | Typ | Null | Default / Computed |
|---|---|---|---|
| OutgoingMessageQueueEventId | bigint | NOT NULL IDENTITY |  |
| Identifier | MAM.utPublicIdentifier=nvarchar(200) | NOT NULL | (lower(CONVERT([nvarchar](50),newid()))) |
| TransactionIdentifier | MAM.utPublicIdentifier=nvarchar(200) | NOT NULL |  |
| Payload | nvarchar(max) | NOT NULL |  |
| TypeName | nvarchar(1024) | NOT NULL |  |
| CreatedAt | datetime2 | NOT NULL | (getutcdate()) |
| Sent | bit | NOT NULL |  |
| MutUser | MAM.utMutUser=nvarchar(200) | NULL |  |
| MutDate | MAM.utMutDate=datetime2 | NULL |  |
| PriorityForced | int | NULL |  |
| PrioritySelector | MAM.utPublicIdentifier=nvarchar(200) | NULL |  |
| EventAction | nvarchar(850) | NULL |  |
| EventActionDetails | nvarchar(max) | NULL |  |
| ObjectIdentifier | MAM.utPublicIdentifier=nvarchar(200) | NULL |  |
| ObjectType | nvarchar(850) | NULL |  |
| PropertyDefinitionIdentifier | MAM.utPublicIdentifier=nvarchar(200) | NULL |  |
| UserIdentifier | MAM.utPublicIdentifier=nvarchar(200) | NULL |  |

- PK: OutgoingMessageQueueEventId
- UNIQUE: Identifier
- IX: TransactionIdentifier

## MAM.PriceList

| Spalte | Typ | Null | Default / Computed |
|---|---|---|---|
| PriceListId | bigint | NOT NULL IDENTITY |  |
| Identifier | MAM.utPublicIdentifier=nvarchar(200) | NOT NULL |  |
| Name | nvarchar(200) | NOT NULL |  |
| Description | nvarchar(max) | NULL |  |
| Status | MAM.utEnum=int | NOT NULL |  |
| CreatedAt | datetime2 | NOT NULL |  |
| CreatedBy | nvarchar(200) | NOT NULL |  |
| UpdatedAt | datetime2 | NOT NULL |  |
| UpdatedBy | nvarchar(200) | NOT NULL |  |
| RowVersion | rowversion | NOT NULL |  |

- PK: PriceListId
- UNIQUE: Identifier

## MAM.PriceListUserContext

| Spalte | Typ | Null | Default / Computed |
|---|---|---|---|
| PriceListUserContextId | bigint | NOT NULL IDENTITY |  |
| PriceListId | bigint | NOT NULL |  |
| UserContextId | bigint | NULL |  |
| Percentage | decimal(18,0) | NOT NULL |  |
| CreatedAt | datetime2 | NOT NULL |  |
| CreatedBy | nvarchar(200) | NOT NULL |  |
| RowVersion | rowversion | NOT NULL |  |
| UserContextGroupId | bigint | NULL |  |

- PK: PriceListUserContextId
- UNIQUE: PriceListId, UserContextId, UserContextGroupId
- FK: PriceListId -> MAM.PriceList(PriceListId)
- FK: UserContextId -> MAM.UserContext(UserContextId)
- FK: UserContextGroupId -> MAM.UserContextGroup(UserContextGroupId)

## MAM.Property

| Spalte | Typ | Null | Default / Computed |
|---|---|---|---|
| PropertyId | bigint | NOT NULL IDENTITY |  |
| Identifier | MAM.utPublicIdentifier=nvarchar(200) | NOT NULL | (lower(CONVERT([nvarchar](50),newid()))) |
| NodeId | bigint | NULL |  |
| EdgeId | bigint | NULL |  |
| PropertyDefinitionId | bigint | NOT NULL |  |
| IsDeleted | bit | NOT NULL |  |
| MutUser | MAM.utMutUser=nvarchar(200) | NULL |  |
| MutDate | MAM.utMutDate=datetime2 | NULL |  |
| RowVersion | rowversion | NOT NULL |  |

- PK: PropertyId
- FK: EdgeId -> MAM.Edge(EdgeId)
- FK: NodeId -> MAM.Node(NodeId)
- FK: PropertyDefinitionId -> MAM.PropertyDefinition(PropertyDefinitionId)
- IX: PropertyDefinitionId
- IX: EdgeId
- IX: UNIQUE Identifier INCLUDE(IsDeleted) WHERE ([IsDeleted]<>(1))
- IX: UNIQUE NodeId,PropertyDefinitionId INCLUDE(IsDeleted) WHERE ([IsDeleted]<>(1) AND [NodeId] IS NOT NULL)
- IX: UNIQUE NodeId,PropertyDefinitionId,Identifier,IsDeleted

## MAM.PropertyDefinition

| Spalte | Typ | Null | Default / Computed |
|---|---|---|---|
| PropertyDefinitionId | bigint | NOT NULL IDENTITY |  |
| Identifier | MAM.utPublicIdentifier=nvarchar(200) | NOT NULL | (lower(CONVERT([nvarchar](50),newid()))) |
| PropertyGroupId | bigint | NOT NULL |  |
| TranslationIdentifier | MAM.utTranslationIdentifier=nvarchar(80) | NOT NULL |  |
| ValueType | MAM.utEnum=int | NOT NULL |  |
| HasListValues | bit | NOT NULL |  |
| Editable | bit | NOT NULL |  |
| AllowMultipleValues | bit | NOT NULL |  |
| AllowCustomListValue | bit | NOT NULL |  |
| SearchOption | MAM.utEnum=int | NOT NULL |  |
| IsLanguageDependant | bit | NOT NULL |  |
| IsSystemProperty | bit | NOT NULL |  |
| IgnoreCopyingOnNewVersion | bit | NOT NULL |  |
| Status | MAM.utEnum=int | NOT NULL |  |
| IsDeleted | bit | NOT NULL |  |
| MutUser | MAM.utMutUser=nvarchar(200) | NULL |  |
| MutDate | MAM.utMutDate=datetime2 | NULL |  |
| RowVersion | rowversion | NOT NULL |  |
| IsSearchableFacet | bit | NOT NULL | ((0)) |
| IsFileNamingUnlocked | bit | NOT NULL | ((0)) |
| HasUserContext | bit | NOT NULL | ((0)) |
| IsUrl | bit | NOT NULL | ((0)) |
| SortKey | int | NULL |  |

- PK: PropertyDefinitionId
- UNIQUE: TranslationIdentifier
- FK: PropertyGroupId -> MAM.PropertyGroup(PropertyGroupId)
- IX: PropertyDefinitionId,Identifier,IsDeleted INCLUDE(PropertyGroupId,TranslationIdentifier,ValueType,HasListValues,Editable,AllowMultipleValues,AllowCustomListValue,SearchOption,IsLanguageDependant,IsSystemProperty,IsSearchableFacet,SortKey)
- IX: UNIQUE Identifier INCLUDE(IsDeleted) WHERE ([IsDeleted]<>(1))

## MAM.PropertyDefinitionNodeType

| Spalte | Typ | Null | Default / Computed |
|---|---|---|---|
| PropertyDefinitionNodeTypeId | bigint | NOT NULL IDENTITY |  |
| PropertyDefinitionId | bigint | NOT NULL |  |
| NodeTypeId | bigint | NOT NULL |  |
| MutUser | MAM.utMutUser=nvarchar(200) | NULL |  |
| MutDate | MAM.utMutDate=datetime2 | NULL |  |
| RowVersion | rowversion | NOT NULL |  |

- PK: PropertyDefinitionNodeTypeId
- UNIQUE: PropertyDefinitionId, NodeTypeId
- FK: NodeTypeId -> MAM.NodeType(NodeTypeId)
- FK: PropertyDefinitionId -> MAM.PropertyDefinition(PropertyDefinitionId)
- IX: PropertyDefinitionId INCLUDE(NodeTypeId)

## MAM.PropertyDefinitionSystemPropertyStrategy

| Spalte | Typ | Null | Default / Computed |
|---|---|---|---|
| PropertyDefinitionSystemPropertyStrategyId | bigint | NOT NULL IDENTITY |  |
| PropertyDefinitionId | bigint | NOT NULL |  |
| NodeTypeId | bigint | NOT NULL |  |
| SystemPropertyStrategy | MAM.utEnum=int | NOT NULL |  |
| MutUser | MAM.utMutUser=nvarchar(200) | NULL |  |
| MutDate | MAM.utMutDate=datetime2 | NULL |  |
| RowVersion | rowversion | NOT NULL |  |

- PK: PropertyDefinitionSystemPropertyStrategyId
- UNIQUE: PropertyDefinitionId, NodeTypeId, SystemPropertyStrategy
- FK: NodeTypeId -> MAM.NodeType(NodeTypeId)
- FK: PropertyDefinitionId -> MAM.PropertyDefinition(PropertyDefinitionId)

## MAM.PropertyGroup

| Spalte | Typ | Null | Default / Computed |
|---|---|---|---|
| PropertyGroupId | bigint | NOT NULL IDENTITY |  |
| Identifier | MAM.utPublicIdentifier=nvarchar(200) | NOT NULL | (lower(CONVERT([nvarchar](50),newid()))) |
| TranslationIdentifier | MAM.utTranslationIdentifier=nvarchar(80) | NOT NULL |  |
| SequenceNr | MAM.utSequenceNr=int | NOT NULL |  |
| Status | MAM.utEnum=int | NOT NULL |  |
| IsDeleted | bit | NOT NULL |  |
| MutUser | MAM.utMutUser=nvarchar(200) | NULL |  |
| MutDate | MAM.utMutDate=datetime2 | NULL |  |
| RowVersion | rowversion | NOT NULL |  |

- PK: PropertyGroupId
- UNIQUE: TranslationIdentifier
- IX: Identifier,IsDeleted INCLUDE(PropertyGroupId,TranslationIdentifier,SequenceNr,Status)
- IX: UNIQUE Identifier INCLUDE(IsDeleted) WHERE ([IsDeleted]<>(1))

## MAM.PropertyImport

| Spalte | Typ | Null | Default / Computed |
|---|---|---|---|
| PropertyImportId | bigint | NOT NULL IDENTITY |  |
| Identifier | MAM.utPublicIdentifier=nvarchar(200) | NOT NULL | (lower(CONVERT([nvarchar](50),newid()))) |
| FileName | MAM.utFilename=nvarchar(850) | NULL |  |
| DataId | MAM.utPublicIdentifier=nvarchar(200) | NULL |  |
| MimeType | MAM.utName=nvarchar(850) | NULL |  |
| Status | MAM.utEnum=int | NOT NULL | ((0)) |
| BackgroundJobId | bigint | NULL |  |
| CreatedAt | MAM.utDateTime=datetime2 | NOT NULL |  |
| CreatedBy | MAM.utMutUser=nvarchar(200) | NOT NULL |  |
| InitiatedAt | MAM.utDateTime=datetime2 | NULL |  |
| FinishedAt | MAM.utDateTime=datetime2 | NULL |  |
| TotalPropertyMutationCount | int | NOT NULL | ((0)) |
| FailedPropertyMutationCount | int | NOT NULL | ((0)) |
| SucceededPropertyMutationCount | int | NOT NULL | ((0)) |
| MutUser | MAM.utMutUser=nvarchar(200) | NULL |  |
| MutDate | MAM.utMutDate=datetime2 | NULL |  |
| RowVersion | rowversion | NOT NULL |  |
| SkippedPropertyMutationCount | int | NOT NULL | ((0)) |

- PK: PropertyImportId
- UNIQUE: Identifier
- FK: BackgroundJobId -> MAM.BackgroundJob(BackgroundJobId)
- IX: BackgroundJobId

## MAM.PropertyImportEntity

| Spalte | Typ | Null | Default / Computed |
|---|---|---|---|
| PropertyImportEntityId | bigint | NOT NULL IDENTITY |  |
| PropertyImportId | bigint | NOT NULL |  |
| EntityIdentifier | MAM.utPublicIdentifier=nvarchar(200) | NOT NULL |  |
| NodeTypeId | bigint | NOT NULL |  |
| MutUser | MAM.utMutUser=nvarchar(200) | NULL |  |
| MutDate | MAM.utMutDate=datetime2 | NULL |  |
| RowVersion | rowversion | NOT NULL |  |
| VersionIdentifier | MAM.utPublicIdentifier=nvarchar(200) | NULL |  |
| VersionFileName | MAM.utFilename=nvarchar(850) | NULL |  |
| VersionNumber | MAM.utNumber=int | NULL |  |

- PK: PropertyImportEntityId
- FK: NodeTypeId -> MAM.NodeType(NodeTypeId)
- FK: PropertyImportId -> MAM.PropertyImport(PropertyImportId)
- IX: PropertyImportId

## MAM.PropertyImportMessage

| Spalte | Typ | Null | Default / Computed |
|---|---|---|---|
| PropertyImportMessageId | bigint | NOT NULL IDENTITY |  |
| PropertyImportId | bigint | NOT NULL |  |
| MessageType | MAM.utEnum=int | NOT NULL |  |
| Severity | MAM.utEnum=int | NOT NULL |  |
| Result | MAM.utDescription=nvarchar(1024) | NULL |  |
| RowNumber | int | NULL |  |
| ColumnNumber | int | NULL |  |
| MutUser | MAM.utMutUser=nvarchar(200) | NULL |  |
| MutDate | MAM.utMutDate=datetime2 | NULL |  |
| RowVersion | rowversion | NOT NULL |  |

- PK: PropertyImportMessageId
- FK: PropertyImportId -> MAM.PropertyImport(PropertyImportId)
- IX: PropertyImportId

## MAM.PropertyImportMutation

| Spalte | Typ | Null | Default / Computed |
|---|---|---|---|
| PropertyImportMutationId | bigint | NOT NULL IDENTITY |  |
| PropertyImportEntityId | bigint | NOT NULL |  |
| PropertyImportId | bigint | NOT NULL |  |
| PropertyDefinitionIdentifier | MAM.utPublicIdentifier=nvarchar(200) | NOT NULL |  |
| Success | bit | NULL |  |
| MutUser | MAM.utMutUser=nvarchar(200) | NULL |  |
| MutDate | MAM.utMutDate=datetime2 | NULL |  |
| RowVersion | rowversion | NOT NULL |  |
| Result | MAM.utEnum=int | NOT NULL | ((0)) |

- PK: PropertyImportMutationId
- FK: PropertyImportId -> MAM.PropertyImport(PropertyImportId)
- FK: PropertyImportEntityId -> MAM.PropertyImportEntity(PropertyImportEntityId)
- IX: PropertyImportEntityId

## MAM.PropertyListValue

| Spalte | Typ | Null | Default / Computed |
|---|---|---|---|
| PropertyListValueId | bigint | NOT NULL IDENTITY |  |
| Identifier | MAM.utPublicIdentifier=nvarchar(200) | NOT NULL | (lower(CONVERT([nvarchar](50),newid()))) |
| PropertyDefinitionId | bigint | NOT NULL |  |
| SortKey | int | NULL |  |
| ValueBool | bit | NULL |  |
| ValueInt | bigint | NULL |  |
| ValueDecimal | decimal(18,10) | NULL |  |
| ValueDate | date | NULL |  |
| ValueDateTime | datetime2 | NULL |  |
| ValueDateTimeDurationTicks | bigint | NULL |  |
| ValueText | nvarchar(1024) | NULL |  |
| TranslationIdentifier | MAM.utTranslationIdentifier=nvarchar(80) | NULL |  |
| IsDeleted | bit | NOT NULL |  |
| MutUser | MAM.utMutUser=nvarchar(200) | NULL |  |
| MutDate | MAM.utMutDate=datetime2 | NULL |  |
| RowVersion | rowversion | NOT NULL |  |
| Status | MAM.utEnum=int | NOT NULL | ((0)) |

- PK: PropertyListValueId
- FK: PropertyDefinitionId -> MAM.PropertyDefinition(PropertyDefinitionId)
- IX: PropertyDefinitionId,IsDeleted INCLUDE(PropertyListValueId,Identifier,SortKey,ValueBool,ValueInt,ValueDecimal,ValueDate,ValueDateTime,ValueDateTimeDurationTicks,ValueText,TranslationIdentifier)
- IX: UNIQUE Identifier,PropertyDefinitionId INCLUDE(IsDeleted) WHERE ([IsDeleted]<>(1))
- IX: UNIQUE TranslationIdentifier WHERE ([TranslationIdentifier] IS NOT NULL)

## MAM.PropertyMassEdit

| Spalte | Typ | Null | Default / Computed |
|---|---|---|---|
| PropertyMassEditId | bigint | NOT NULL IDENTITY |  |
| Identifier | MAM.utPublicIdentifier=nvarchar(200) | NOT NULL | (lower(CONVERT([nvarchar](50),newid()))) |
| Status | MAM.utEnum=int | NOT NULL | ((0)) |
| BackgroundJobId | bigint | NULL |  |
| CreatedAt | MAM.utDateTime=datetime2 | NOT NULL |  |
| CreatedBy | MAM.utMutUser=nvarchar(200) | NOT NULL |  |
| InitiatedAt | MAM.utDateTime=datetime2 | NULL |  |
| FinishedAt | MAM.utDateTime=datetime2 | NULL |  |
| TotalPropertyMutationCount | int | NOT NULL | ((0)) |
| FailedPropertyMutationCount | int | NOT NULL | ((0)) |
| SucceededPropertyMutationCount | int | NOT NULL | ((0)) |
| MutUser | MAM.utMutUser=nvarchar(200) | NULL |  |
| MutDate | MAM.utMutDate=datetime2 | NULL |  |
| RowVersion | rowversion | NOT NULL |  |
| TotalAssetCount | bigint | NOT NULL |  |
| ApplicableAssetCountLimit | int | NOT NULL |  |

- PK: PropertyMassEditId
- UNIQUE: Identifier
- FK: BackgroundJobId -> MAM.BackgroundJob(BackgroundJobId)
- IX: BackgroundJobId

## MAM.PropertyMassEditAsset

| Spalte | Typ | Null | Default / Computed |
|---|---|---|---|
| PropertyMassEditAssetId | bigint | NOT NULL IDENTITY |  |
| PropertyMassEditId | bigint | NOT NULL |  |
| AssetIdentifier | MAM.utPublicIdentifier=nvarchar(200) | NOT NULL |  |
| AssetVersionIdentifier | MAM.utPublicIdentifier=nvarchar(200) | NULL |  |
| AssetVersionFileName | MAM.utFilename=nvarchar(850) | NULL |  |
| AssetVersionNumber | MAM.utNumber=int | NOT NULL | ((0)) |
| MutUser | MAM.utMutUser=nvarchar(200) | NULL |  |
| MutDate | MAM.utMutDate=datetime2 | NULL |  |
| RowVersion | rowversion | NOT NULL |  |

- PK: PropertyMassEditAssetId
- FK: PropertyMassEditId -> MAM.PropertyMassEdit(PropertyMassEditId)
- IX: PropertyMassEditId

## MAM.PropertyMassEditMutation

| Spalte | Typ | Null | Default / Computed |
|---|---|---|---|
| PropertyMassEditMutationId | bigint | NOT NULL IDENTITY |  |
| PropertyMassEditId | bigint | NOT NULL |  |
| PropertyDefinitionIdentifier | MAM.utPublicIdentifier=nvarchar(200) | NOT NULL |  |
| PropertyListItemIdentifier | MAM.utPublicIdentifier=nvarchar(200) | NULL |  |
| DataLanguageIdentifier | MAM.utPublicIdentifier=nvarchar(200) | NULL |  |
| Value | nvarchar(max) | NULL |  |
| UserContextIdentifier | MAM.utPublicIdentifier=nvarchar(200) | NULL |  |
| PropertyStrategy | MAM.utEnum=int | NULL |  |
| PropertyValueStrategy | MAM.utEnum=int | NULL |  |
| TranslationStrategy | MAM.utEnum=int | NULL |  |
| MutUser | MAM.utMutUser=nvarchar(200) | NULL |  |
| MutDate | MAM.utMutDate=datetime2 | NULL |  |
| RowVersion | rowversion | NOT NULL |  |

- PK: PropertyMassEditMutationId
- FK: PropertyMassEditId -> MAM.PropertyMassEdit(PropertyMassEditId)
- IX: PropertyMassEditId
- CHECK: [PropertyStrategy] IS NOT NULL AND [PropertyValueStrategy] IS NULL AND [TranslationStrategy] IS NULL OR [PropertyStrategy] IS NULL AND [PropertyValueStrategy] IS NOT NULL AND [TranslationStrategy] IS NULL OR [PropertyStrategy] IS NULL AND [PropertyValueStrategy] IS NULL AND [TranslationStrategy] IS NOT NULL

## MAM.PropertyMassEditResult

| Spalte | Typ | Null | Default / Computed |
|---|---|---|---|
| PropertyMassEditResultId | bigint | NOT NULL IDENTITY |  |
| PropertyMassEditAssetId | bigint | NOT NULL |  |
| PropertyMassEditMutationId | bigint | NOT NULL |  |
| PropertyIdentifier | nvarchar(200) | NULL |  |
| Result | MAM.utEnum=int | NOT NULL | ((0)) |
| MutUser | MAM.utMutUser=nvarchar(200) | NULL |  |
| MutDate | MAM.utMutDate=datetime2 | NULL |  |
| RowVersion | rowversion | NOT NULL |  |

- PK: PropertyMassEditResultId
- UNIQUE: PropertyMassEditAssetId, PropertyMassEditMutationId
- FK: PropertyMassEditAssetId -> MAM.PropertyMassEditAsset(PropertyMassEditAssetId)
- FK: PropertyMassEditMutationId -> MAM.PropertyMassEditMutation(PropertyMassEditMutationId)
- IX: PropertyMassEditAssetId
- IX: PropertyMassEditMutationId

## MAM.PropertyNodeTypeInheritance

| Spalte | Typ | Null | Default / Computed |
|---|---|---|---|
| PropertyNodeTypeInheritanceId | bigint | NOT NULL IDENTITY |  |
| PropertyId | bigint | NOT NULL |  |
| NodeTypeId | bigint | NOT NULL |  |
| MutUser | MAM.utMutUser=nvarchar(200) | NULL |  |
| MutDate | MAM.utMutDate=datetime2 | NULL |  |
| RowVersion | rowversion | NOT NULL |  |

- PK: PropertyNodeTypeInheritanceId
- UNIQUE: PropertyId, NodeTypeId
- FK: NodeTypeId -> MAM.NodeType(NodeTypeId)
- FK: PropertyId -> MAM.Property(PropertyId)

## MAM.PropertySet

| Spalte | Typ | Null | Default / Computed |
|---|---|---|---|
| PropertySetId | bigint | NOT NULL IDENTITY |  |
| Identifier | MAM.utPublicIdentifier=nvarchar(200) | NOT NULL | (lower(CONVERT([nvarchar](50),newid()))) |
| NodeTypeIdentifier | MAM.utPublicIdentifier=nvarchar(200) | NOT NULL |  |
| Name | nvarchar(1024) | NOT NULL |  |
| CreatedBy | MAM.utMutUser=nvarchar(200) | NULL |  |
| IsGlobal | bit | NOT NULL |  |
| MutUser | MAM.utMutUser=nvarchar(200) | NULL |  |
| MutDate | MAM.utMutDate=datetime2 | NULL |  |
| RowVersion | rowversion | NOT NULL |  |
| IsDefault | bit | NOT NULL | ((0)) |

- PK: PropertySetId
- UNIQUE: Identifier
- IX: UNIQUE NodeTypeIdentifier,IsDefault WHERE ([IsDefault]=(1))

## MAM.PropertySetCondition

| Spalte | Typ | Null | Default / Computed |
|---|---|---|---|
| PropertySetConditionId | bigint | NOT NULL IDENTITY |  |
| PropertySetId | bigint | NOT NULL |  |
| PropertyDefinitionId | bigint | NOT NULL |  |
| PropertyListValueId | bigint | NOT NULL |  |
| MutUser | MAM.utMutUser=nvarchar(200) | NULL |  |
| MutDate | MAM.utMutDate=datetime2 | NULL |  |
| RowVersion | rowversion | NOT NULL |  |

- PK: PropertySetConditionId
- FK: PropertyDefinitionId -> MAM.PropertyDefinition(PropertyDefinitionId)
- FK: PropertyListValueId -> MAM.PropertyListValue(PropertyListValueId)
- FK: PropertySetId -> MAM.PropertySet(PropertySetId)

## MAM.PropertySetPropertyDefinitions

| Spalte | Typ | Null | Default / Computed |
|---|---|---|---|
| PropertySetPropertyDefinitionsId | bigint | NOT NULL IDENTITY |  |
| PropertySetId | bigint | NULL |  |
| MutUser | MAM.utMutUser=nvarchar(200) | NULL |  |
| MutDate | MAM.utMutDate=datetime2 | NULL |  |
| RowVersion | rowversion | NOT NULL |  |
| PropertyDefinitionId | bigint | NULL |  |
| FillMode | MAM.utEnum=int | NOT NULL | ((0)) |

- PK: PropertySetPropertyDefinitionsId
- FK: PropertyDefinitionId -> MAM.PropertyDefinition(PropertyDefinitionId)
- FK: PropertySetId -> MAM.PropertySet(PropertySetId)

## MAM.PropertyValue

| Spalte | Typ | Null | Default / Computed |
|---|---|---|---|
| PropertyValueId | bigint | NOT NULL IDENTITY |  |
| Identifier | MAM.utPublicIdentifier=nvarchar(200) | NOT NULL | (lower(CONVERT([nvarchar](50),newid()))) |
| PropertyId | bigint | NOT NULL |  |
| PropertyListValueId | bigint | NULL |  |
| ValueBool | bit | NULL |  |
| ValueInt | bigint | NULL |  |
| ValueDecimal | decimal(18,10) | NULL |  |
| ValueDate | date | NULL |  |
| ValueDateTime | datetime2 | NULL |  |
| ValueDateTimeDurationTicks | bigint | NULL |  |
| ValueText | nvarchar(max) | NULL |  |
| TranslationIdentifier | MAM.utTranslationIdentifier=nvarchar(80) | NULL |  |
| IsDeleted | bit | NOT NULL |  |
| MutUser | MAM.utMutUser=nvarchar(200) | NULL |  |
| MutDate | MAM.utMutDate=datetime2 | NULL |  |
| RowVersion | rowversion | NOT NULL |  |
| UserContextId | bigint | NULL |  |

- PK: PropertyValueId
- FK: UserContextId -> MAM.UserContext(UserContextId)
- FK: PropertyId -> MAM.Property(PropertyId)
- FK: PropertyListValueId -> MAM.PropertyListValue(PropertyListValueId)
- IX: PropertyId
- IX: PropertyId INCLUDE(PropertyValueId,Identifier,PropertyListValueId,ValueBool,ValueInt,ValueDecimal,ValueDate,ValueDateTime,ValueDateTimeDurationTicks,ValueText,TranslationIdentifier,IsDeleted)
- IX: PropertyListValueId
- IX: UserContextId
- IX: UNIQUE Identifier INCLUDE(TranslationIdentifier,IsDeleted) WHERE ([IsDeleted]<>(1))
- IX: UNIQUE TranslationIdentifier WHERE ([TranslationIdentifier] IS NOT NULL)

## MAM.RightsOwner

| Spalte | Typ | Null | Default / Computed |
|---|---|---|---|
| RightsOwnerId | bigint | NOT NULL IDENTITY |  |
| NodeId | bigint | NOT NULL |  |
| Name | nvarchar(200) | NOT NULL |  |
| BillingContactId | bigint | NOT NULL |  |
| AvatarFileReferenceId | bigint | NULL |  |
| Status | MAM.utEnum=int | NOT NULL |  |
| CreatedAt | datetime2 | NOT NULL |  |
| CreatedBy | nvarchar(200) | NOT NULL |  |
| UpdatedAt | datetime2 | NOT NULL |  |
| UpdatedBy | nvarchar(200) | NOT NULL |  |
| MutUser | MAM.utMutUser=nvarchar(200) | NULL |  |
| MutDate | MAM.utMutDate=datetime2 | NULL |  |
| RowVersion | rowversion | NOT NULL |  |

- PK: RightsOwnerId
- FK: BillingContactId -> MAM.BillingContact(BillingContactId)
- FK: AvatarFileReferenceId -> MAM.FileReference(FileReferenceId)
- FK: NodeId -> MAM.Node(NodeId)

## MAM.SavedSearch

| Spalte | Typ | Null | Default / Computed |
|---|---|---|---|
| SavedSearchId | bigint | NOT NULL IDENTITY |  |
| Identifier | MAM.utPublicIdentifier=nvarchar(200) | NOT NULL | (lower(CONVERT([nvarchar](50),newid()))) |
| Name | MAM.utName=nvarchar(850) | NOT NULL |  |
| SearchQuery | nvarchar(max) | NOT NULL |  |
| CreatedBy | MAM.utMutUser=nvarchar(200) | NOT NULL |  |
| MutUser | MAM.utMutUser=nvarchar(200) | NULL |  |
| MutDate | MAM.utMutDate=datetime2 | NULL |  |
| RowVersion | rowversion | NOT NULL |  |

- PK: SavedSearchId
- UNIQUE: Identifier

## MAM.SearchReIndex

| Spalte | Typ | Null | Default / Computed |
|---|---|---|---|
| SearchReIndexId | bigint | NOT NULL IDENTITY |  |
| Identifier | MAM.utPublicIdentifier=nvarchar(200) | NOT NULL | (lower(CONVERT([nvarchar](50),newid()))) |
| InitiatedBy | nvarchar(250) | NOT NULL |  |
| InitiatedAt | datetime2 | NOT NULL | (getutcdate()) |
| FinishedAt | datetime2 | NULL |  |
| EntityTypeIdentifiers | nvarchar(400) | NOT NULL |  |
| Status | MAM.utEnum=int | NOT NULL |  |
| FailureReason | nvarchar(400) | NULL |  |
| TotalEntitiesCount | bigint | NOT NULL |  |
| SuccessfulEntitiesCount | bigint | NOT NULL |  |
| FailedEntitiesCount | bigint | NOT NULL |  |
| IsCancelled | bit | NOT NULL | ((0)) |
| RowVersion | rowversion | NOT NULL |  |

- PK: SearchReIndexId
- UNIQUE: Identifier

## MAM.SearchReIndexFailedEntities

| Spalte | Typ | Null | Default / Computed |
|---|---|---|---|
| SearchReIndexFailedEntitiesId | bigint | NOT NULL IDENTITY |  |
| SearchReIndexId | bigint | NOT NULL |  |
| NodeIdentifier | MAM.utPublicIdentifier=nvarchar(200) | NOT NULL |  |
| NodeTypeIdentifier | nvarchar(200) | NOT NULL |  |
| DisplayName | nvarchar(200) | NOT NULL |  |
| BatchNumber | int | NOT NULL |  |

- PK: SearchReIndexFailedEntitiesId
- FK: SearchReIndexId -> MAM.SearchReIndex(SearchReIndexId)

## MAM.Share

| Spalte | Typ | Null | Default / Computed |
|---|---|---|---|
| ShareId | bigint | NOT NULL IDENTITY |  |
| Identifier | MAM.utPublicIdentifier=nvarchar(200) | NOT NULL | (lower(CONVERT([nvarchar](50),newid()))) |
| Name | nvarchar(200) | NOT NULL |  |
| WorkspaceNodeId | bigint | NOT NULL |  |
| ValidUntil | datetime2 | NOT NULL |  |
| IsDeleted | bit | NOT NULL |  |
| MutUser | MAM.utMutUser=nvarchar(200) | NULL |  |
| MutDate | MAM.utMutDate=datetime2 | NULL |  |
| RowVersion | rowversion | NOT NULL |  |

- PK: ShareId
- UNIQUE: Identifier
- FK: WorkspaceNodeId -> MAM.Node(NodeId)
- IX: Identifier,IsDeleted WHERE ([IsDeleted]<>(1))

## MAM.ShareAssetTypeDerivationType

| Spalte | Typ | Null | Default / Computed |
|---|---|---|---|
| ShareAssetTypeDerivationTypeId | bigint | NOT NULL IDENTITY |  |
| ShareId | bigint | NOT NULL |  |
| AssetTypeId | bigint | NOT NULL |  |
| DerivationTypeId | bigint | NOT NULL |  |
| MutUser | MAM.utMutUser=nvarchar(200) | NULL |  |
| MutDate | MAM.utMutDate=datetime2 | NULL |  |
| RowVersion | rowversion | NOT NULL |  |

- PK: ShareAssetTypeDerivationTypeId
- FK: AssetTypeId -> MAM.AssetType(AssetTypeId)
- FK: DerivationTypeId -> MAM.DerivationType(DerivationTypeId)
- FK: ShareId -> MAM.Share(ShareId)

## MAM.ShareRecipient

| Spalte | Typ | Null | Default / Computed |
|---|---|---|---|
| ShareRecipientId | bigint | NOT NULL IDENTITY |  |
| ShareId | bigint | NOT NULL |  |
| EmailAddress | nvarchar(254) | NOT NULL |  |
| Token | nvarchar(200) | NOT NULL |  |
| IsDeleted | bit | NOT NULL |  |
| MutUser | MAM.utMutUser=nvarchar(200) | NULL |  |
| MutDate | MAM.utMutDate=datetime2 | NULL |  |
| RowVersion | rowversion | NOT NULL |  |

- PK: ShareRecipientId
- UNIQUE: Token
- FK: ShareId -> MAM.Share(ShareId)
- IX: UNIQUE ShareId,EmailAddress INCLUDE(IsDeleted) WHERE ([IsDeleted]<>(1))

## MAM.State

| Spalte | Typ | Null | Default / Computed |
|---|---|---|---|
| StateId | bigint | NOT NULL IDENTITY |  |
| StateNodeId | bigint | NOT NULL |  |
| SequenceNr | MAM.utSequenceNr=int | NOT NULL |  |
| TranslationIdentifier | MAM.utTranslationIdentifier=nvarchar(80) | NOT NULL |  |
| MutUser | MAM.utMutUser=nvarchar(200) | NULL |  |
| MutDate | MAM.utMutDate=datetime2 | NULL |  |
| RowVersion | rowversion | NOT NULL |  |
| IsDefault | bit | NOT NULL | ((0)) |

- PK: StateId
- FK: StateNodeId -> MAM.Node(NodeId)
- IX: StateNodeId INCLUDE(StateId,TranslationIdentifier)

## MAM.Task

| Spalte | Typ | Null | Default / Computed |
|---|---|---|---|
| TaskId | bigint | NOT NULL IDENTITY |  |
| NodeId | bigint | NOT NULL |  |
| Name | nvarchar(400) | NOT NULL |  |
| Description | nvarchar(max) | NULL |  |
| DueDate | date | NULL |  |
| CreatedBy | MAM.utMutUser=nvarchar(200) | NOT NULL |  |
| CreatedAt | datetime2 | NOT NULL |  |
| MutUser | MAM.utMutUser=nvarchar(200) | NULL |  |
| MutDate | MAM.utMutDate=datetime2 | NULL |  |
| RowVersion | rowversion | NOT NULL |  |
| TaskTypeId | bigint | NULL |  |
| AssignedUserId | bigint | NULL |  |
| Status | MAM.utEnum=int | NOT NULL | ((0)) |
| SequenceNr | MAM.utSequenceNr=int | NOT NULL |  |

- PK: TaskId
- FK: NodeId -> MAM.Node(NodeId)
- FK: AssignedUserId -> Admin.User(UserId)
- FK: TaskTypeId -> MAM.TaskType(TaskTypeId)

## MAM.TaskType

| Spalte | Typ | Null | Default / Computed |
|---|---|---|---|
| TaskTypeId | bigint | NOT NULL IDENTITY |  |
| Identifier | MAM.utPublicIdentifier=nvarchar(200) | NOT NULL | (lower(CONVERT([nvarchar](50),newid()))) |
| PropertyListValueIdentifier | nvarchar(200) | NOT NULL | ('') |
| MutUser | MAM.utMutUser=nvarchar(200) | NULL |  |
| MutDate | MAM.utMutDate=datetime2 | NULL |  |
| RowVersion | rowversion | NOT NULL |  |

- PK: TaskTypeId
- UNIQUE: Identifier
- UNIQUE: PropertyListValueIdentifier

## MAM.Translation

| Spalte | Typ | Null | Default / Computed |
|---|---|---|---|
| TranslationId | bigint | NOT NULL IDENTITY |  |
| Identifier | MAM.utPublicIdentifier=nvarchar(200) | NOT NULL | (lower(CONVERT([nvarchar](50),newid()))) |
| ObjectType | MAM.utEnum=int | NOT NULL |  |
| TranslationIdentifier | MAM.utTranslationIdentifier=nvarchar(80) | NOT NULL | (CONVERT([nvarchar](50),newid())) |
| FieldName | MAM.utName=nvarchar(850) | NOT NULL |  |
| LanguageId | bigint | NOT NULL |  |
| TranslatedValue | nvarchar(max) | NOT NULL |  |
| MutUser | MAM.utMutUser=nvarchar(200) | NULL |  |
| MutDate | MAM.utMutDate=datetime2 | NULL |  |
| RowVersion | rowversion | NOT NULL |  |

- PK: TranslationId
- UNIQUE: Identifier
- UNIQUE: ObjectType, TranslationIdentifier, FieldName, LanguageId
- FK: LanguageId -> MAM.Language(LanguageId)
- IX: ObjectType,TranslationIdentifier,FieldName,LanguageId INCLUDE(TranslationId,Identifier,TranslatedValue)

## MAM.UploadSession

| Spalte | Typ | Null | Default / Computed |
|---|---|---|---|
| UploadSessionId | bigint | NOT NULL IDENTITY |  |
| SessionIdentifier | nvarchar(255) | NOT NULL |  |
| UploadType | MAM.utEnum=int | NOT NULL |  |
| FileName | MAM.utFilename=nvarchar(850) | NULL |  |
| AssetSetNodeId | bigint | NULL |  |
| AssetVersionNodeId | bigint | NULL |  |
| UploadEntityNodeId | bigint | NULL |  |
| TaskNodeId | bigint | NULL |  |
| WorkspaceNodeId | bigint | NULL |  |
| DerivationTypeId | bigint | NULL |  |
| MutDate | MAM.utMutDate=datetime2 | NULL |  |
| ArchiveExtractionMode | MAM.utEnum=int | NULL |  |
| DeleteAssetAfterExtraction | bit | NOT NULL | ((0)) |

- PK: UploadSessionId
- UNIQUE: SessionIdentifier
- FK: DerivationTypeId -> MAM.DerivationType(DerivationTypeId)
- FK: AssetSetNodeId -> MAM.Node(NodeId)
- FK: AssetVersionNodeId -> MAM.Node(NodeId)
- FK: TaskNodeId -> MAM.Node(NodeId)
- FK: UploadEntityNodeId -> MAM.Node(NodeId)
- FK: WorkspaceNodeId -> MAM.Node(NodeId)
- CHECK: [UploadType]=(1) AND [AssetSetNodeId] IS NULL AND [AssetVersionNodeId] IS NULL AND [UploadEntityNodeId] IS NOT NULL AND [TaskNodeId] IS NULL AND [WorkspaceNodeId] IS NULL AND [DerivationTypeId] IS NULL OR [UploadType]=(2) AND [AssetSetNodeId] IS NOT NULL AND [AssetVersionNodeId] IS NULL AND [UploadEntityNodeId] IS NULL AND [TaskNodeId] IS NULL AND [WorkspaceNodeId] IS NULL AND [DerivationTypeId] IS NULL OR [UploadType]=(3) AND [AssetSetNodeId] IS NULL AND [AssetVersionNodeId] IS NULL AND [UploadEntityNodeId] IS NOT NULL AND [TaskNodeId] IS NULL AND [WorkspaceNodeId] IS NOT NULL AND [DerivationTypeId] IS NULL OR [UploadType]=(4) AND [AssetSetNodeId] IS NULL AND [AssetVersionNodeId] IS NULL AND [UploadEntityNodeId] IS NOT NULL AND [TaskNodeId] IS NOT NULL AND [WorkspaceNodeId] IS NOT NULL AND [DerivationTypeId] IS NULL OR [UploadType]=(5) AND [AssetSetNodeId] IS NOT NULL AND [AssetVersionNodeId] IS NULL AND [UploadEntityNodeId] IS NULL AND [TaskNodeId] IS NULL AND [WorkspaceNodeId] IS NULL AND [DerivationTypeId] IS NOT NULL OR [UploadType]=(6) AND [AssetSetNodeId] IS NOT NULL AND [AssetVersionNodeId] IS NOT NULL AND [UploadEntityNodeId] IS NULL AND [TaskNodeId] IS NULL AND [WorkspaceNodeId] IS NULL AND [DerivationTypeId] IS NOT NULL

## MAM.UploadSessionChunk

| Spalte | Typ | Null | Default / Computed |
|---|---|---|---|
| UploadSessionChunkId | bigint | NOT NULL IDENTITY |  |
| UploadSessionId | bigint | NOT NULL |  |
| ChunkIdentifier | nvarchar(255) | NOT NULL |  |
| MutDate | MAM.utMutDate=datetime2 | NULL |  |

- PK: UploadSessionChunkId
- FK: UploadSessionId -> MAM.UploadSession(UploadSessionId)

## MAM.UserContext

| Spalte | Typ | Null | Default / Computed |
|---|---|---|---|
| UserContextId | bigint | NOT NULL IDENTITY |  |
| Identifier | MAM.utPublicIdentifier=nvarchar(200) | NOT NULL | (lower(CONVERT([nvarchar],newid()))) |
| TranslationIdentifier | MAM.utTranslationIdentifier=nvarchar(80) | NOT NULL |  |
| SequenceNr | int | NULL |  |
| IsDefault | bit | NOT NULL | ((0)) |
| IsDeleted | bit | NOT NULL | ((0)) |
| MutUser | MAM.utMutUser=nvarchar(200) | NULL |  |
| MutDate | MAM.utMutDate=datetime2 | NULL |  |
| RowVersion | rowversion | NOT NULL |  |

- PK: UserContextId
- IX: UNIQUE Identifier,IsDeleted WHERE ([IsDeleted]<>(1))

## MAM.UserContextGroup

| Spalte | Typ | Null | Default / Computed |
|---|---|---|---|
| UserContextGroupId | bigint | NOT NULL IDENTITY |  |
| Identifier | MAM.utPublicIdentifier=nvarchar(200) | NOT NULL |  |
| TranslationIdentifier | MAM.utTranslationIdentifier=nvarchar(80) | NOT NULL |  |
| CreatedAt | MAM.utMutDate=datetime2 | NOT NULL |  |
| RowVersion | rowversion | NOT NULL |  |

- PK: UserContextGroupId
- UNIQUE: Identifier

## MAM.UserContextGroupUserContext

| Spalte | Typ | Null | Default / Computed |
|---|---|---|---|
| UserContextGroupUserContext | bigint | NOT NULL IDENTITY |  |
| UserContextGroupId | bigint | NOT NULL |  |
| UserContextId | bigint | NOT NULL |  |

- PK: UserContextGroupUserContext
- UNIQUE: UserContextGroupId, UserContextId
- FK: UserContextGroupId -> MAM.UserContextGroup(UserContextGroupId)
- FK: UserContextId -> MAM.UserContext(UserContextId)

## MAM.WatermarkDefinition

| Spalte | Typ | Null | Default / Computed |
|---|---|---|---|
| WatermarkDefinitionId | bigint | NOT NULL IDENTITY |  |
| Identifier | MAM.utPublicIdentifier=nvarchar(200) | NOT NULL | (lower(CONVERT([nvarchar],newid()))) |
| DisplayInUI | bit | NOT NULL | ((0)) |
| DerivationJobDefinitionId | bigint | NOT NULL |  |
| PropertyDefinitionId | bigint | NOT NULL |  |
| MutUser | MAM.utMutUser=nvarchar(200) | NULL |  |
| MutDate | MAM.utMutDate=datetime2 | NULL |  |
| RowVersion | rowversion | NOT NULL |  |

- PK: WatermarkDefinitionId
- FK: DerivationJobDefinitionId -> MAM.DerivationJobDefinition(DerivationJobDefinitionId)
- FK: PropertyDefinitionId -> MAM.PropertyDefinition(PropertyDefinitionId)

## MAM.WatermarkTrigger

| Spalte | Typ | Null | Default / Computed |
|---|---|---|---|
| WatermarkTriggerId | bigint | NOT NULL IDENTITY |  |
| Identifier | MAM.utPublicIdentifier=nvarchar(200) | NOT NULL | (lower(CONVERT([nvarchar],newid()))) |
| WatermarkDefinitionId | bigint | NULL |  |
| PropertyListValueId | bigint | NULL |  |
| ValueBool | bit | NULL |  |
| FileReferenceId | bigint | NOT NULL |  |
| MutUser | MAM.utMutUser=nvarchar(200) | NULL |  |
| MutDate | MAM.utMutDate=datetime2 | NULL |  |
| RowVersion | rowversion | NOT NULL |  |

- PK: WatermarkTriggerId
- FK: FileReferenceId -> MAM.FileReference(FileReferenceId)
- FK: PropertyListValueId -> MAM.PropertyListValue(PropertyListValueId)
- FK: WatermarkDefinitionId -> MAM.WatermarkDefinition(WatermarkDefinitionId)

## MAM.Workspace

| Spalte | Typ | Null | Default / Computed |
|---|---|---|---|
| WorkspaceId | bigint | NOT NULL IDENTITY |  |
| WorkspaceNodeId | bigint | NOT NULL |  |
| Status | MAM.utEnum=int | NOT NULL |  |
| TranslationIdentifier | MAM.utTranslationIdentifier=nvarchar(80) | NOT NULL |  |
| CreatedBy | MAM.utMutUser=nvarchar(200) | NOT NULL |  |
| MutUser | MAM.utMutUser=nvarchar(200) | NULL |  |
| MutDate | MAM.utMutDate=datetime2 | NULL |  |
| RowVersion | rowversion | NOT NULL |  |
| IsTemplate | bit | NOT NULL | ((0)) |
| PropertySetIdentifier | MAM.utPublicIdentifier=nvarchar(200) | NULL |  |
| FinalizationCategoryId | bigint | NULL |  |
| CopyPropertiesToAssets | bit | NOT NULL | ((0)) |
| WorkspaceType | MAM.utEnum=int | NOT NULL |  |
| CreatedAt | MAM.utDateTime=datetime2 | NOT NULL |  |
| SequenceNr | MAM.utSequenceNr=int | NOT NULL |  |
| AvatarFileReferenceId | bigint | NULL |  |

- PK: WorkspaceId
- FK: AvatarFileReferenceId -> MAM.FileReference(FileReferenceId)
- FK: WorkspaceNodeId -> MAM.Node(NodeId)
- FK: FinalizationCategoryId -> MAM.Category(CategoryId)
- IX: WorkspaceNodeId INCLUDE(WorkspaceId,TranslationIdentifier)

# Schema `STH`


## STH.Chunk

| Spalte | Typ | Null | Default / Computed |
|---|---|---|---|
| ChunkId | bigint | NOT NULL IDENTITY |  |
| Identifier | nvarchar(255) | NOT NULL |  |
| ChunkDataId | nvarchar(255) | NOT NULL |  |
| ChunkSequenceNumber | bigint | NOT NULL |  |
| SessionId | bigint | NOT NULL |  |
| StorageId | bigint | NOT NULL |  |
| MutDate | STH.utMutDate=datetime2 | NULL |  |

- PK: ChunkId
- UNIQUE: Identifier
- FK: SessionId -> STH.Session(SessionId)
- FK: StorageId -> STH.Storage(StorageId)
- IX: StorageId
- IX: SessionId

## STH.Data

| Spalte | Typ | Null | Default / Computed |
|---|---|---|---|
| DataId | bigint | NOT NULL IDENTITY |  |
| Identifier | nvarchar(255) | NOT NULL |  |
| Name | nvarchar(255) | NOT NULL |  |
| Extension | nvarchar(255) | NOT NULL |  |
| FileHash | nvarchar(850) | NULL |  |
| HashAlgorithm | int | NOT NULL | ((0)) |
| FileSize | bigint | NOT NULL | ((0)) |
| StorageId | bigint | NOT NULL |  |
| MimeType | nvarchar(850) | NULL |  |
| MutDate | STH.utMutDate=datetime2 | NULL |  |

- PK: DataId
- UNIQUE: Identifier
- FK: StorageId -> STH.Storage(StorageId)

## STH.Session

| Spalte | Typ | Null | Default / Computed |
|---|---|---|---|
| SessionId | bigint | NOT NULL IDENTITY |  |
| Identifier | nvarchar(255) | NOT NULL |  |
| MutDate | STH.utMutDate=datetime2 | NULL |  |

- PK: SessionId
- UNIQUE: Identifier

## STH.Storage

| Spalte | Typ | Null | Default / Computed |
|---|---|---|---|
| StorageId | bigint | NOT NULL IDENTITY |  |
| Identifier | nvarchar(255) | NOT NULL |  |
| ConfigurationType | int | NOT NULL |  |
| Configuration | nvarchar(max) | NOT NULL |  |
| ConfigurationVersion | int | NOT NULL |  |
| MutDate | STH.utMutDate=datetime2 | NULL |  |

- PK: StorageId
- UNIQUE: Identifier

## STH.SyncTest

| Spalte | Typ | Null | Default / Computed |
|---|---|---|---|
| Id | int | NOT NULL IDENTITY |  |
| title | varchar(100) | NULL |  |
| currency | decimal(10,2) | NULL |  |
| added2 | varchar(50) | NULL |  |

- PK: Id

# Schema `dbo`


## dbo.SchemaVersionsSharedien

| Spalte | Typ | Null | Default / Computed |
|---|---|---|---|
| Id | int | NOT NULL IDENTITY |  |
| ScriptName | nvarchar(255) | NOT NULL |  |
| Applied | datetime | NOT NULL |  |

- PK: Id

## dbo.SchemaVersionsStorageHandler

| Spalte | Typ | Null | Default / Computed |
|---|---|---|---|
| Id | int | NOT NULL IDENTITY |  |
| ScriptName | nvarchar(255) | NOT NULL |  |
| Applied | datetime | NOT NULL |  |

- PK: Id

## dbo.group_membership

| Spalte | Typ | Null | Default / Computed |
|---|---|---|---|
| group_id | nvarchar(36) | NOT NULL |  |
| member_id | nvarchar(36) | NOT NULL |  |
| member_type | nvarchar(8) | NOT NULL |  |
| authorities | nvarchar(255) | NULL |  |
| added | datetime | NOT NULL |  |
| origin | nvarchar(36) | NOT NULL |  |
| identity_zone_id | nvarchar(36) | NULL |  |
| id | int | NOT NULL |  |

## dbo.groups

| Spalte | Typ | Null | Default / Computed |
|---|---|---|---|
| id | nvarchar(36) | NOT NULL |  |
| displayname | nvarchar(255) | NOT NULL |  |
| created | datetime | NOT NULL |  |
| lastmodified | datetime | NULL |  |
| version | int | NOT NULL |  |
| identity_zone_id | nvarchar(36) | NOT NULL |  |
| description | nvarchar(255) | NULL |  |

## dbo.users

| Spalte | Typ | Null | Default / Computed |
|---|---|---|---|
| id | char(36) | NOT NULL |  |
| created | datetime | NOT NULL |  |
| lastmodified | datetime | NULL |  |
| version | bigint | NOT NULL |  |
| username | nvarchar(255) | NOT NULL |  |
| password | nvarchar(255) | NOT NULL |  |
| email | nvarchar(255) | NOT NULL |  |
| givenname | nvarchar(255) | NULL |  |
| familyname | nvarchar(255) | NULL |  |
| active | bit | NOT NULL |  |
| phonenumber | nvarchar(255) | NULL |  |
| authorities | nvarchar(1024) | NULL |  |
| verified | bit | NOT NULL |  |
| origin | nvarchar(36) | NOT NULL |  |
| external_id | nvarchar(255) | NULL |  |
| identity_zone_id | nvarchar(36) | NOT NULL |  |
| salt | nvarchar(36) | NULL |  |
| passwd_lastmodified | datetime | NULL |  |
| legacy_verification_behavior | bit | NOT NULL |  |
| passwd_change_required | bit | NOT NULL |  |
| last_logon_success_time | bigint | NULL |  |
| previous_logon_success_time | bigint | NULL |  |

# Views


## Admin.RoleView

```sql
SELECT [RoleId],
           [Identifier],
           [TranslationIdentifier],
           [Status],
           [IsDeleted],
           [IsSystem],
           [PropertyMassEditEntityLimit],
           [PropertyExportEntityLimit],
           [MutUser],
           [MutDate],
           [RowVersion]
    FROM Admin.Role
    WHERE IsDeleted = 0
```

## Admin.TenantView

```sql
SELECT 
		[TenantId], 
		[Identifier], 
		[Status], 
		[IsDefaultTenant], 
		[TranslationIdentifier], 
		[IsDeleted], 
		[MutUser], 
		[MutDate], 
		[RowVersion], 
		[AcceptsRegistrations] 
	FROM Admin.Tenant 
	WHERE IsDeleted = 0
```

## Admin.UserGroupView

```sql
SELECT 
		[UserGroupId], 
		[Identifier], 
		[Name], 
		[Description], 
		[IsDeleted], 
		[MutUser], 
		[MutDate], 
		[RowVersion] 
	FROM Admin.UserGroup 
	WHERE IsDeleted = 0
```

## Admin.UserView

```sql
SELECT
          [UserId]
         ,[Identifier]
         ,[UserName]
         ,[FirstName]
         ,[LastName]
         ,[EmailAddress]
         ,[Department]
         ,[Status]
         ,[IsDeleted]
         ,[PasswordSalt]
         ,[PasswordHash]
         ,[ProviderName]
         ,[ProviderSubjectId]
         ,[MutUser]
         ,[MutDate]
         ,[RowVersion]
         ,[LastLoginAttemptDate]
         ,[LastLoginDate]
         ,[LastPasswordResetDate]
         ,[CreatedAt]
         ,[Company]
         ,[ContactPerson]
         ,[PhoneNumber]
         ,[StatusChangeDate]
         ,[AccessType]
    FROM Admin.[User]
    WHERE IsDeleted = 0
```

## MAM.DerivationTypeView

```sql
SELECT
		[DerivationTypeId], 
		[Identifier], 
		[MutUser], 
		[MutDate], 
		[RowVersion], 
		[TranslationIdentifier], 
		[IsSystem], 
		[IsDeleted]
	FROM MAM.DerivationType 
	WHERE IsDeleted = 0
```

## Mam.EdgeTypeView

```sql
SELECT
        EdgeTypeId,
        Identifier,
        EdgeSemantic,
        TranslationIdentifier,
        Status,
        IsDirected,
        IsSystem,
        IsDeleted,
        MutUser,
        MutDate,
        RowVersion,
        IsUsedForAssetSearch
    FROM Mam.EdgeType
    WHERE IsDeleted = 0
```

## MAM.LanguageView

```sql
SELECT
		[LanguageId], 
		[Identifier], 
		[Status], 
		[IsDefaultLanguage], 
		[TranslationIdentifier], 
		[IsDeleted], 
		[MutUser], 
		[MutDate], 
		[RowVersion] 
	FROM MAM.Language 
	WHERE IsDeleted = 0
```

## MAM.NodeTypeView

```sql
SELECT 
		[NodeTypeId], 
		[Identifier], 
		[NodeSemantic], 
		[MutUser], 
		[MutDate], 
		[RowVersion], 
		[TranslationIdentifier], 
		[Status], 
		[IsSystem], 
		[IsDeleted]
	FROM MAM.NodeType 
	WHERE IsDeleted = 0
```

## MAM.NodeView

```sql
SELECT
		[NodeId], 
		[Identifier], 
		[NodeTypeId], 
		[SequenceNr], 
		[TenantId], 
		[IsDeleted], 
		[CreateUser], 
		[CreateDate], 
		[MutUser], 
		[MutDate], 
		[RowVersion], 
		[InterruptInheritance] 
	FROM MAM.Node 
	WHERE IsDeleted = 0
```

## MAM.PropertyDefinitionView

```sql
SELECT
         [PropertyDefinitionId]
        ,[Identifier]
        ,[PropertyGroupId]
        ,[TranslationIdentifier]
        ,[ValueType]
        ,[HasListValues]
        ,[Editable]
        ,[AllowMultipleValues]
        ,[AllowCustomListValue]
        ,[SearchOption]
        ,[IsLanguageDependant]
        ,[IsSystemProperty]
        ,[IgnoreCopyingOnNewVersion]
        ,[Status]
        ,[IsDeleted]
        ,[MutUser]
        ,[MutDate]
        ,[RowVersion]
        ,[IsSearchableFacet]
        ,[IsFileNamingUnlocked]
        ,[HasUserContext]
        ,[IsUrl]
        ,[SortKey]
    FROM [MAM].[PropertyDefinition]
    WHERE [IsDeleted] = 0
```

## MAM.PropertyGroupView

```sql
SELECT
		[PropertyGroupId], 
		[Identifier], 
		[TranslationIdentifier], 
		[SequenceNr], 
		[Status], 
		[IsDeleted], 
		[MutUser], 
		[MutDate], 
		[RowVersion] 
	FROM MAM.PropertyGroup 
	WHERE IsDeleted = 0
```

## MAM.PropertyListValueView

```sql
SELECT
        [PropertyListValueId],
        [Identifier],
        [PropertyDefinitionId],
        [SortKey],
        [ValueBool],
        [ValueInt],
        [ValueDecimal],
        [ValueDate],
        [ValueDateTime],
        [ValueDateTimeDurationTicks],
        [ValueText],
        [TranslationIdentifier],
        [IsDeleted],
        [MutUser],
        [MutDate],
        [RowVersion],
        [Status]
    FROM [MAM].[PropertyListValue]
    WHERE [IsDeleted] = 0
```

## MAM.PropertyValueView

```sql
SELECT 
		[PropertyValueId], 
		[Identifier], 
		[PropertyId], 
		[PropertyListValueId], 
		[ValueBool], 
		[ValueInt], 
		[ValueDecimal], 
		[ValueDate], 
		[ValueDateTime], 
		[ValueDateTimeDurationTicks], 
		[ValueText], 
		[TranslationIdentifier], 
		[IsDeleted], 
		[MutUser], 
		[MutDate], 
		[RowVersion], 
		[UserContextId]
	FROM MAM.PropertyValue 
	WHERE IsDeleted = 0
```

## MAM.PropertyView

```sql
SELECT 
		[PropertyId], 
		[Identifier], 
		[NodeId], 
		[EdgeId], 
		[PropertyDefinitionId], 
		[IsDeleted], 
		[MutUser], 
		[MutDate], 
		[RowVersion]
	FROM MAM.Property 
	WHERE IsDeleted = 0
```

## MAM.ShareRecipientView

```sql
SELECT 
		[ShareRecipientId], 
		[ShareId], 
		[EmailAddress], 
		[Token], 
		[IsDeleted], 
		[MutUser], 
		[MutDate], 
		[RowVersion]
	FROM MAM.ShareRecipient 
	WHERE IsDeleted = 0
```

## MAM.ShareView

```sql
SELECT 
		[ShareId], 
		[Identifier], 
		[Name], 
		[WorkspaceNodeId], 
		[ValidUntil], 
		[IsDeleted], 
		[MutUser], 
		[MutDate], 
		[RowVersion] 
	FROM MAM.Share 
	WHERE IsDeleted = 0
```

## MAM.UserContextView

```sql
SELECT 
		[UserContextId], 
		[Identifier], 
		[TranslationIdentifier], 
		[SequenceNr], 
		[IsDefault], 
		[IsDeleted], 
		[MutUser], 
		[MutDate], 
		[RowVersion] 
	FROM MAM.UserContext 
	WHERE IsDeleted = 0
```