extern alias LiteDbBase;

#nullable enable

using System;
using System.Linq.Expressions;
using System.Reflection;
using BaseLiteDB = LiteDbBase::LiteDB;

namespace LiteDB.Spatial
{
    /// <summary>
    /// Provides spatial configuration helpers for the LiteDB entity builder.
    /// </summary>
    public static class SpatialEntityBuilderExtensions
    {
        /// <summary>
        /// Registers spatial options for the specified member using a builder callback.
        /// </summary>
        public static BaseLiteDB.EntityBuilder<T> WithSpatialOptions<T, TValue>(
            this BaseLiteDB.EntityBuilder<T> builder,
            Expression<Func<T, TValue>> member,
            Action<SpatialMemberOptionsBuilder> configure)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            if (configure == null)
            {
                throw new ArgumentNullException(nameof(configure));
            }

            var memberInfo = ResolveMember(member);
            var optionsBuilder = new SpatialMemberOptionsBuilder();
            configure(optionsBuilder);

            SpatialMemberOptionsRegistry.Register(memberInfo, optionsBuilder.Build());
            return builder;
        }

        /// <summary>
        /// Registers spatial options for the specified member using a pre-built configuration object.
        /// </summary>
        public static BaseLiteDB.EntityBuilder<T> WithSpatialOptions<T, TValue>(
            this BaseLiteDB.EntityBuilder<T> builder,
            Expression<Func<T, TValue>> member,
            SpatialMemberOptions options)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            var memberInfo = ResolveMember(member);
            SpatialMemberOptionsRegistry.Register(memberInfo, options);
            return builder;
        }

        private static MemberInfo ResolveMember<T, TValue>(Expression<Func<T, TValue>> expression)
        {
            var memberInfo = SpatialMemberExpressionResolver.GetMemberInfo(expression);
            return memberInfo ?? throw new ArgumentException("Spatial configuration expressions must target a field or property.", nameof(expression));
        }
    }
}
