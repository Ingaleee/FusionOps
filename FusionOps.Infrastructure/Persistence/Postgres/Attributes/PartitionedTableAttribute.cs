using System;

namespace FusionOps.Infrastructure.Persistence.Postgres.Attributes;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public sealed class PartitionedTableAttribute : Attribute
{
    public string Strategy { get; }
    public PartitionedTableAttribute(string strategy) => Strategy = strategy;
} 