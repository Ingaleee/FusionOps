using System;

namespace FusionOps.Domain.Attributes;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public sealed class PartitionedTableAttribute : Attribute
{
    public string Strategy { get; }
    public PartitionedTableAttribute(string strategy) => Strategy = strategy;
}