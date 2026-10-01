using Coworkee.Core.Security;
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Shareme.Infrastructure;

public sealed class ShareMeDbContext(DbContextOptions<ShareMeDbContext> options, ICurrentUser currentUser, IEnumerable<IModelContributor> contributors)
    : CoworkeeDbContext(options, currentUser, contributors);
