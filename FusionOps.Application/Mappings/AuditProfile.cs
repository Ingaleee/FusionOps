using AutoMapper;
using FusionOps.Application.Dto;
using FusionOps.Infrastructure.Persistence.Postgres;

namespace FusionOps.Application.Mappings;

public class AuditProfile : Profile
{
    public AuditProfile()
    {
        CreateMap<AllocationHistoryRow, AllocationHistoryDto>();
    }
}
