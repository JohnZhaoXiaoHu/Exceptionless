﻿using Exceptionless.Core.Extensions;
using Exceptionless.Core.Models;
using Exceptionless.Core.Repositories.Configuration;
using Exceptionless.Core.Repositories.Queries;
using FluentValidation;
using Foundatio.Repositories;
using Foundatio.Repositories.Models;
using Foundatio.Utility;
using Nest;

namespace Exceptionless.Core.Repositories;

public class ProjectRepository : RepositoryOwnedByOrganization<Project>, IProjectRepository {
    public ProjectRepository(ExceptionlessElasticConfiguration configuration, IValidator<Project> validator, AppOptions options)
        : base(configuration.Projects, validator, options) {
    }

    public Task<CountResult> GetCountByOrganizationIdAsync(string organizationId) {
        if (String.IsNullOrEmpty(organizationId))
            throw new ArgumentNullException(nameof(organizationId));

        return CountAsync(q => q.Organization(organizationId), o => o.Cache(String.Concat("Organization:", organizationId)));
    }

    public Task<FindResults<Project>> GetByOrganizationIdsAsync(ICollection<string> organizationIds, CommandOptionsDescriptor<Project> options = null) {
        if (organizationIds == null)
            throw new ArgumentNullException(nameof(organizationIds));

        if (organizationIds.Count == 0)
            return Task.FromResult(new FindResults<Project>());

        return FindAsync(q => q.Organization(organizationIds).SortAscending(p => p.Name.Suffix("keyword")), options);
    }

    public Task<FindResults<Project>> GetByFilterAsync(AppFilter systemFilter, string userFilter, string sort, CommandOptionsDescriptor<Project> options = null) {
        IRepositoryQuery<Project> query = new RepositoryQuery<Project>()
            .AppFilter(systemFilter)
            .FilterExpression(userFilter);

        query = !String.IsNullOrEmpty(sort) ? query.SortExpression(sort) : query.SortAscending(p => p.Name.Suffix("keyword"));
        return FindAsync(q => query, options);
    }

    public Task<FindResults<Project>> GetByNextSummaryNotificationOffsetAsync(byte hourToSendNotificationsAfterUtcMidnight, int limit = 50) {
        var filter = Query<Project>.Range(r => r.Field(o => o.NextSummaryEndOfDayTicks).LessThan(SystemClock.UtcNow.Ticks - (TimeSpan.TicksPerHour * hourToSendNotificationsAfterUtcMidnight)));
        return FindAsync(q => q.ElasticFilter(filter).SortAscending(p => p.OrganizationId), o => o.SearchAfterPaging().PageLimit(limit));
    }

    public async Task IncrementNextSummaryEndOfDayTicksAsync(IReadOnlyCollection<Project> projects) {
        if (projects == null)
            throw new ArgumentNullException(nameof(projects));

        if (projects.Count == 0)
            return;

        string script = $"ctx._source.next_summary_end_of_day_ticks += {TimeSpan.TicksPerDay}L;";
        await PatchAsync(projects.Select(p => p.Id).ToArray(), new ScriptPatch(script), o => o.Notifications(false)).AnyContext();
        await InvalidateCacheAsync(projects).AnyContext();
    }

    protected override Task InvalidateCacheAsync(IReadOnlyCollection<ModifiedDocument<Project>> documents, ChangeType? changeType = null) {
        var organizations = documents.Select(d => d.Value.OrganizationId).Distinct().Where(id => !String.IsNullOrEmpty(id));
        return Task.WhenAll(Cache.RemoveAllAsync(organizations.Select(id => $"count:Organization:{id}")), base.InvalidateCacheAsync(documents, changeType));
    }
}
