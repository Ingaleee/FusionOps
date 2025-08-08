using AutoMapper;
using FusionOps.Application.Dto;

namespace FusionOps.Application.Mappings;

public class AuditProfile : Profile
{
    public AuditProfile()
    {
        // Маппинг выполняется проекцией в хэндлере, чтобы избежать зависимости Application -> Infrastructure
    }
}
