namespace LiteDB
{
    /// <summary>
    /// All supported BsonTypes in sort order
    /// </summary>
    public enum BsonType : byte
    {
        MinValue = 0,

        Null = 1,

        Int32 = 2,
        Int64 = 3,
        Double = 4,
        Decimal = 5,

        String = 6,

        Document = 7,
        Array = 8,

        Binary = 9,
        ObjectId = 10,
        Guid = 11,

        Boolean = 12,
        DateTime = 13,

        MaxValue = 14,

        /// <summary>
        /// DEPRECATED: Vector type support is moving to LiteDB.Vector plugin.
        /// This value is kept for backward compatibility with existing databases.
        /// New code should use the LiteDB.Vector plugin which registers custom BSON types.
        /// </summary>
        [Obsolete("Vector support is moving to LiteDB.Vector plugin. This will be removed in a future version.")]
        Vector = 100,
    }
}