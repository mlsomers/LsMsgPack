[Setup wizard](README.md) › [Saved](Files.md) › Only my own app

# Question 3: must old files keep working in new versions of your app?

This is the question of *backward compatibility*: can version 2 of your app read what version 1 wrote?

Your classes change over time. You add a `DueDate` property to `Invoice`, remove an old `Fax` field, rename `Customer` to `Client`, or split `Shape` into `Circle` and `Square`. A file written before that change still has the old shape. Whether that matters depends on how long a file lives.

### Yes: files are kept (documents, save games, archives)

A user saves a document today and opens it after the next three updates of your app. Losing their work is not an option.

The default settings already handle the common changes (added, removed and reordered properties). The setup page explains which changes are safe, which need care, and how to test that old files still load.

**→ [Long-lived documents](Setup-Documents.md)**

### No: files are thrown away (caches, temp files)

The data can be recreated at any time: a cache entry that expires after ten minutes, a temp file that's deleted when the app closes, a computed result that's rebuilt after a deploy. If a new version can't read an old entry, it simply recalculates it.

Then you can choose the smallest and fastest format, and make sure a new version never reads an old entry by mistake.

**→ [Caches and temporary files](Setup-Cache.md)**

### Not sure?

Choose *yes*. The document setup is still fast and small; the cache setup only squeezes out the last bit, and it reads old data wrongly (rather than failing) when you forget to separate versions.
