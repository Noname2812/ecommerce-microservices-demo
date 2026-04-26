using Shared.Application;
using Shared.Kernel.Primitives;
using UrbanX.Identity.Domain;

namespace UrbanX.Identity.Application.Usecases.V1.Query;

public sealed class ListAuditLogsQueryHandler : IQueryHandler<ListAuditLogsQuery, PageResult<AuthAuditLogDto>>
{
    private readonly IAuthAuditLogRepository _repository;

    public ListAuditLogsQueryHandler(IAuthAuditLogRepository repository) => _repository = repository;

    public async Task<Result<PageResult<AuthAuditLogDto>>> Handle(ListAuditLogsQuery request, CancellationToken cancellationToken)
    {
        var page = await _repository.ListAsync(
            request.UserId,
            request.EventType,
            request.From,
            request.To,
            request.PageIndex,
            request.PageSize,
            cancellationToken);

        var dtoItems = page.Items.Select(x => new AuthAuditLogDto(
            x.Id, x.UserId, x.Email, x.EventType, x.IpAddress, x.UserAgent, x.MetadataJson, x.OccurredAt
        )).ToList();

        return Result.Success(PageResult<AuthAuditLogDto>.Create(dtoItems, page.PageIndex, page.PageSize, page.TotalCount));
    }
}
