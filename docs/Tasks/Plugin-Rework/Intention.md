
# Key guidelines
## Safety
- Plugins cannot break existing databases (master: pre-vector and pre-spatial).
- Potential dataloss or inconsistency cannot happen and should be guarded against by appropriate guards.
- Plugins should not be required if the database works without them, even if the database was created or modified with the plugin loaded.
- If part of the database requires the plugin to work properly (eg a collection created with the plugin needs plugin specific code), the database should have the neccesary fields to indicate that the plugin is required, to either open the collection or the entire database at all.
- If rebuilding the database would require the plugin to prevent data loss, the database should not open without the plugin at all.
- If rebuilding could lead to loss of indexes, it could be acceptable, and the plugin should be able to rebuild the indexes once loaded again, but that behaviour should be explicitly enabled by the user (rebuilding an index could take some time and the user knows best if they want to do it or not).

## Modularity
- Plugins should be modular and self-contained, allowing for easy addition and removal without affecting the core.
- Plugin specific code should not be required in the core library and kept minimal if absolutely necessary.
- Plugins should be able to define their own data structures and operations without modifying the core database code.


# Design ideas

The current inintialization of a LiteDB can stay as it is, but refactored if seems neccessary.
For plugins, we want to change the initialization process to a builder pattern.

Eg:

```csharp
var db = new LiteDatabaseBuilder()
    .UsePlugin(new MyPlugin())
    .UsePlugin(new MyOtherPlugin())
    .UseInMemory() //.UseFile("mydb.db") // etc
    .Build(); // .BuildFactory() if we want to build a factory instead of the database directly. Holds the configuration for the database and can be used to create multiple databases with the same configuration.
```

This would turn around the current design of shared litedb engines a bit. The state of shared engines would live in the factory.
