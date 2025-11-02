# Feature Specification: Vector Search Plugin Migration

**Feature Branch**: `001-vector-plugin-migration`  
**Created**: 2025-11-02  
**Status**: Draft  
**Input**: User description: "Migrate all vector(search) related code from the core LiteDB library into the LiteDB.Vector library by utilizing the new LiteDB infrastructure"

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Existing Applications Continue Working (Priority: P1)

Applications using vector search features continue to work without any code changes after the migration. Users simply need to reference the vector extension package and enable it during database initialization.

**Why this priority**: This is the most critical requirement because any breaking changes would prevent existing users from upgrading. The goal of this migration is architectural improvement without disrupting user workflows.

**Independent Test**: Install the vector extension package, enable it when creating a database instance, and verify that existing vector index creation, insertion, querying, and deletion operations produce identical results to the current implementation.

**Acceptance Scenarios**:

1. **Given** an application using vector indexes with the current implementation, **When** the application is updated to use the vector extension, **Then** all vector index operations (create, query, update, delete) produce identical results
2. **Given** a database with existing vector indexes created before the migration, **When** the database is opened with the new extension-based implementation, **Then** existing indexes remain functional and queryable
3. **Given** code using index creation APIs with vector options, **When** executed against the extension-based implementation, **Then** the index is created successfully with the same behavior
4. **Given** code using vector distance operators in queries (e.g., `VECTOR_DIST` or the optional `VECTOR_SIM` alias), **When** executed against the extension-based implementation, **Then** the returned scores match the prior implementation

---

### User Story 2 - Vector Index Lifecycle Management (Priority: P1)

Developers can create, query, and manage vector indexes through the same APIs, with all operations handled by the extension infrastructure.

**Why this priority**: This ensures the core vector index functionality works correctly in the new architecture. It's essential for the feature to be usable.

**Independent Test**: Create a vector index on a collection field, insert documents with vector embeddings, query using vector similarity search, and verify results match expected nearest neighbors. Drop the index and verify cleanup is complete.

**Acceptance Scenarios**:

1. **Given** a collection with documents containing vector embeddings, **When** an index is created on the vector field, **Then** a graph-based search structure is built for efficient similarity queries
2. **Given** a vector index exists, **When** documents are inserted with vector data, **Then** the index automatically maintains its search structure
3. **Given** a vector index with embedded documents, **When** a similarity search is performed with a query vector, **Then** results are returned ordered by distance using the configured metric
4. **Given** a vector index exists, **When** the index is dropped, **Then** all index structures are removed and resources are released

---

### User Story 3 - Distance Metric Support (Priority: P2)

Users can specify different distance metrics (Cosine, Euclidean, DotProduct) when creating vector indexes, and queries return appropriate distance/similarity scores.

**Why this priority**: Different use cases require different distance metrics (e.g., normalized embeddings use cosine, unnormalized use euclidean). This is important for flexibility but not blocking for basic functionality.

**Independent Test**: Create three separate vector indexes using different metrics (Cosine, Euclidean, DotProduct), insert the same test data, query with the same vector, and verify that distance scores differ appropriately based on the mathematical properties of each metric.

**Acceptance Scenarios**:

1. **Given** a vector index created with Cosine metric, **When** a distance search is performed, **Then** results include cosine distance values between 0 and 2 (0 = identical, 1 = orthogonal)
2. **Given** a vector index created with Euclidean metric, **When** a distance search is performed, **Then** results include Euclidean distance values
3. **Given** a vector index created with DotProduct metric, **When** a distance search is performed, **Then** results are ranked by dot product scores or similarity, depending on configured normalization
4. **Given** multiple indexes with different metrics exist, **When** the same query vector is used, **Then** result rankings may differ based on the metric's properties

---

### User Story 4 - Expression Function Registration (Priority: P2)

The vector distance operator works correctly in query expressions for computing vector distance at query time (outside of indexed searches) and exposes an explicit similarity alias when needed.

**Why this priority**: This enables non-indexed vector comparisons in queries, which is useful for filtering and computed columns. Important for completeness but less critical than indexed search.

**Independent Test**: Write a query using the vector distance function (`VECTOR_DIST`) in a where clause or projection without using an index, and verify the distance score is computed correctly using cosine distance. Repeat with `VECTOR_SIM` to ensure the similarity alias returns the expected inverted score.

**Acceptance Scenarios**:

1. **Given** documents with vector fields, **When** a query uses the distance function to compare two vector fields, **Then** the cosine distance score is returned
2. **Given** a query with the distance function in a where clause, **When** executed, **Then** results are filtered based on the numeric distance threshold
3. **Given** a query projection using the distance function, **When** executed, **Then** the result includes computed distance values for each document
4. **Given** the optional similarity alias is used, **When** executed, **Then** the result equals `1 - distance` for cosine metrics (or documented equivalent for other metrics)

---

### User Story 5 - Extension Method Availability (Priority: P3)

Developers can use convenient extension methods for vector search operations on collections and queryables for a fluent API experience.

**Why this priority**: These methods provide convenience and discoverability but are not essential since the same functionality is achievable through standard query APIs. Nice-to-have for developer experience.

**Independent Test**: Call the vector search extension method to find top 10 nearest neighbors, project the distance using `.WithVectorScore(...)`, then compose queryable extension methods with LINQ operators and verify integration works correctly.

**Acceptance Scenarios**:

1. **Given** a collection with a vector index, **When** the vector search extension method is called, **Then** nearest neighbors are returned efficiently along with their distance scores
2. **Given** a queryable with vector data, **When** the nearest neighbors method is used, **Then** the query is correctly translated to use the vector index and surfaces distance via `.WithVectorScore`
3. **Given** extension methods are used in combination with other LINQ operators (e.g., `OrderByNearest`, `Select`), **When** executed, **Then** query composition works correctly and distance ordering remains stable

---

### Edge Cases

- What happens when a vector index is created but the extension is not installed? System must provide a clear error message indicating the vector extension package is required.
- How does the system handle documents with vectors of incorrect dimensions? Index operations should validate dimension compatibility and reject mismatched vectors with descriptive errors.
- What happens when attempting to create a vector index on a non-vector field? The system should validate that the field contains array or vector data types before creating the index.
- How does the system handle concurrent vector index modifications during queries? The existing snapshot-based isolation model should ensure query consistency.
- What happens when dropping a collection that has vector indexes? All associated vector index pages and structures should be cleaned up automatically.
- How does the system handle migration of existing databases with vector indexes from the previous implementation? Existing indexes should remain functional without requiring recreation (or provide clear migration path if recreation is needed).
- How are ties resolved when multiple documents share the same computed distance? Results must be deterministic by ordering ties on `_id` ascending after sorting by distance (with configurable secondary key for explicit indexes).
- What happens when vector dimensions exceed the UInt16 limit (65,535)? Index creation and query execution must fail fast with a documented error while leaving existing data unchanged.
- How should the engine behave when no vector index is available for a distance query? It must fall back to a full scan and the documentation should explain the expected performance characteristics.
- How are NaN/Infinity values or mixed numeric types in vector literals handled? The engine should reject them with clear errors (or coerce when safe) and document the behavior.
- How are upgrade scenarios handled when metric configuration changes (e.g., existing index expects cosine but query requests Euclidean)? The planner must either reject the query or fall back to scan with an explicit warning.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST separate vector search functionality into an optional extension package that can be installed independently
- **FR-002**: System MUST maintain vector indexing capabilities including graph-based nearest neighbor search algorithms
- **FR-003**: System MUST automatically update vector indexes when documents containing vector data are inserted, updated, or deleted
- **FR-004**: System MUST support creation of vector indexes on array or vector fields with configurable dimensions
- **FR-005**: System MUST enable vector similarity calculations in query expressions for comparing vector fields
- **FR-006**: System MUST preserve existing search quality and performance characteristics for nearest neighbor queries
- **FR-007**: System MUST support multiple distance calculation methods including cosine similarity, euclidean distance, and dot product scoring
- **FR-008**: System MUST validate that vector dimensions match the index configuration during all index operations
- **FR-009**: System MUST maintain backward compatibility with existing public APIs for creating, querying, and managing vector indexes
- **FR-010**: System MUST provide clear error messages when vector index operations are attempted without the required extension installed
- **FR-011**: System MUST preserve all existing test coverage to ensure no functionality regressions occur
- **FR-012**: System MUST maintain vector data types as core functionality since vectors are fundamental data structures
- **FR-013**: System MUST allow the extension package to access necessary internal database structures for efficient index management
- **FR-014**: System MUST automatically maintain index consistency when document changes occur
- **FR-015**: System MUST detect missing vector extension during operations and guide users to install the required package

### Key Entities *(include if feature involves data)*

#### Additional Functional Requirements and Clarifications

**FR-016**: System MUST provide explicit error codes and messages for all vector index and search failures, including dimension mismatch, unsupported field types, and extension absence
**FR-017**: System MUST document and enforce resource limits for vector index size, query result count, and memory usage, with clear diagnostics when limits are exceeded
**FR-018**: System MUST provide diagnostic and logging capabilities for vector index operations, including index creation, query execution, and error conditions
**FR-019**: System MUST guarantee that vector distance and similarity scores and rankings are consistent, well-defined, and documented for each supported metric
**FR-020**: System MUST provide a performance baseline and regression test for vector search operations, with results published in release notes
**FR-021**: Query surfaces MUST expose vector distance scores (fluent APIs, LINQ pipelines, SQL projections) without requiring duplicate computation
**FR-022**: Vector search results MUST be deterministically ordered first by computed distance and then by `_id` ascending (unless the caller specifies a secondary key)
**FR-023**: Vector operators and functions MUST accept an explicit metric parameter and define the SQL grammar, precedence, literal syntax, and numeric coercion rules
**FR-024**: Vector search execution MUST apply an exact distance filter and resorting stage after approximate index traversal to guarantee correctness
**FR-025**: Vector dimensions MUST be capped at `ushort.MaxValue` (65,535) with explicit validation during index creation, document writes, and ad-hoc queries
**FR-026**: Vector helper APIs MUST include convenience builders (e.g., `Vector.Create`, `Vector.Normalize`) so callers are not forced to construct `BsonVector` manually
**FR-027**: Documentation and packaging MUST describe installing `LiteDB.Vector`, registering the plugin via the `plugins` constructor parameter, and highlight naming/metric semantics

##### Score Semantics & Error Handling

- All vector distance and similarity scores MUST be documented with their mathematical range and interpretation (e.g., cosine distance: 0..2 with similarity alias returning cosine values in [-1,1], euclidean distance: >=0, dot product: unbounded with optional normalization notes)
- When the vector extension is not installed, any attempt to use vector index or similarity features MUST fail with a clear error code and message guiding the user to install the required package (NuGet: LiteDB.Vector)
- All error codes and messages MUST be included in the documentation and test plan

##### Plugin Absence & Registration

- If the vector extension is not registered, all vector index and search operations MUST fail gracefully with actionable guidance
- The NuGet package ID for the extension MUST be documented as `LiteDB.Vector`
- Registration steps MUST be included in upgrade and migration documentation

##### Query Syntax & Naming

- `VECTOR_DIST` MUST be the canonical infix operator and function exposed by the plugin; it returns the metric-specific distance where lower values indicate closer neighbors
- `VECTOR_SIM` MAY be provided as an alias that maps to `1 - distance` for cosine metrics (and documented equivalents for metrics that support similarity transforms); if the alias is enabled the XML docs MUST clearly state the semantics
- Function form MUST support an optional metric argument: `VECTOR_DIST($.Embedding, [1,0], 'cosine')`; omitting the metric uses the index configuration or connection-string default
- The infix form MUST follow additive precedence (evaluated before comparison operators but after arithmetic) and associate left-to-right
- Vector literals MUST accept arrays of numeric constants (int, long, double, decimal, float) and coerce to `float` with overflow detection and NaN rejection

##### Resource Limits & Diagnostics

- Maximum vector index size, query result count, and memory usage MUST be documented and enforced
- When limits are exceeded, the system MUST provide diagnostic messages and log entries
- Diagnostic and logging capabilities MUST be testable and included in the test plan

##### Query Planner Behavior

- Vector index scans MUST return candidate sets that are subsequently filtered by the exact metric calculation and re-ordered by distance before results are yielded
- When no vector index is available, the planner MUST fall back to a full collection scan using the specified metric while honoring `maxDistance`, `LIMIT`, and `ORDER BY` semantics
- `ORDER BY VECTOR_DIST(...) LIMIT k` MUST leverage the vector index when available; otherwise the engine MUST document the expected performance characteristics

##### Test Coverage Additions

- Add integration tests that cover both indexed and non-indexed query paths, ensuring fallback scans respect distance thresholds and ordering
- Add tests that target vector dimensions near the UInt16 cap and verify failures when the limit is exceeded
- Add tests that cover vector literals with mixed numeric types, NaN, and Infinity values to ensure consistent coercion or rejection
- Add metric-specific correctness tests (cosine, Euclidean, dot product) for both `WhereNear`/`TopKNear` and SQL `VECTOR_DIST`/`VECTOR_SIM`
- Add determinism tests that assert tie-breaking order remains stable across executions
- Add concurrency tests for vector reads during concurrent writes and index rebuild scenarios
- Add upgrade tests that simulate metric changes and confirm the engine either rejects incompatible configurations or rebuilds as documented

##### Performance Baseline

- Vector search operations MUST be benchmarked and results published in release notes
- Performance regression tests MUST be included in the test plan

- **Vector Index Configuration**: Stores settings for a vector index including the field being indexed, number of dimensions, and distance calculation method. Persisted in the database.
- **Vector Graph Node**: Represents a connection point in the similarity graph structure, maintaining links to nearby similar vectors for efficient searching. Stored in specialized database pages.
- **Vector Index Page**: Database page structures optimized for storing graph connections and vector search metadata.
- **Vector Search Extension**: Optional package providing vector indexing and search capabilities through a registration mechanism.
- **Distance Metric**: Determines how similarity between vectors is calculated - supports angle-based (cosine), distance-based (euclidean), and magnitude-based (dot product) methods.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: All existing vector search tests pass without modification after architectural changes
- **SC-002**: Applications using vector search can upgrade with minimal configuration changes (adding extension registration) and zero changes to query or indexing code
- **SC-003**: Vector search operations (insert, query, delete) perform within 5% of previous implementation speed
- **SC-004**: Database files with existing vector indexes remain compatible and queryable after system updates
- **SC-005**: When vector extension is not installed, operations fail with clear guidance messages
- **SC-006**: Test coverage for vector functionality remains at or above current levels
- **SC-007**: Queries that order or filter by vector distance return deterministic, reproducible results across runs and architectures
- **SC-008**: Documentation published with the package includes installation, plugin registration (code and connection-string forms), naming semantics, and metric selection guidance

### Assumptions

- The extension infrastructure provides sufficient access to internal database structures required for vector index implementation
- Existing graph algorithm parameters for similarity search do not need to be configurable in the initial migration
- The core library will continue to include vector page type definitions and basic structure
- Database format compatibility is maintained - existing vector indexes use the same storage format
- The spatial extension migration provides a proven pattern for implementing index extensions that can be followed for vectors
- Any necessary extension points in the infrastructure can be added if missing (similar to what was done for spatial features)
