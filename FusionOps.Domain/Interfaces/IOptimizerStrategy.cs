using System.Collections.Generic;
using System.Threading.Tasks;
using FusionOps.Domain.Entities;

namespace FusionOps.Domain.Interfaces;

public interface IOptimizerStrategy
{
    Task<IReadOnlyCollection<Allocation>> AllocateAsync(IReadOnlyCollection<HumanResource> humans,
                                                        IReadOnlyCollection<EquipmentResource> equipment,
                                                        int requiredHumans,
                                                        int requiredEquipment);
}