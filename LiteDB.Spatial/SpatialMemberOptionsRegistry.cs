#nullable enable

using System;
using System.Collections.Concurrent;
using System.Reflection;

namespace LiteDB.Spatial
{
    internal static class SpatialMemberOptionsRegistry
    {
        private static readonly ConcurrentDictionary<MemberInfo, SpatialMemberOptions> _registry = new ConcurrentDictionary<MemberInfo, SpatialMemberOptions>();

        public static void Register(MemberInfo member, SpatialMemberOptions options)
        {
            if (member == null)
            {
                throw new ArgumentNullException(nameof(member));
            }

            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            _registry[member] = options;
        }

        public static SpatialMemberOptions? Resolve(Type? declaringType, string? memberName)
        {
            if (declaringType == null || string.IsNullOrWhiteSpace(memberName))
            {
                return null;
            }

            var member = FindMember(declaringType, memberName);
            if (member == null)
            {
                return null;
            }

            if (_registry.TryGetValue(member, out var cached))
            {
                return cached;
            }

            var attribute = member.GetCustomAttribute<SpatialOptionsAttribute>(inherit: true);
            if (attribute == null)
            {
                return null;
            }

            var resolved = attribute.ToOptions();
            _registry[member] = resolved;
            return resolved;
        }

        private static MemberInfo? FindMember(Type type, string memberName)
        {
            const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy;

            var property = type.GetProperty(memberName, Flags);
            if (property != null)
            {
                return property;
            }

            return type.GetField(memberName, Flags);
        }
    }
}
