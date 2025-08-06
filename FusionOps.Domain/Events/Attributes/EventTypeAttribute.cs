using System;

namespace FusionOps.Domain.Events.Attributes;

[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class EventTypeAttribute : Attribute
{
    public string Name { get; }
    public EventTypeAttribute(string name) => Name = name;
}