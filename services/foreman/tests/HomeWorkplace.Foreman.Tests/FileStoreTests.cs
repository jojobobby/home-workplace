using System.Text.Json;
using HomeWorkplace.Foreman;

namespace HomeWorkplace.Foreman.Tests;

public class FileStoreTests
{
    private static FileStore NewStore(out string dataPath)
    {
        dataPath = Path.Combine(Path.GetTempPath(), "foreman-filestore", Guid.NewGuid().ToString("N"));
        return new FileStore(new ForemanOptions { DataPath = dataPath });
    }

    private static TaskModel Task(int n) => new()
    {
        Id = "same-id", Title = $"v{n}", Brief = "b", Assignee = "ada",
        Room = "task-same-id", Workspace = "/w",
    };

    // Regression: a run finishing (ApplyResult → Save) while a /reset or /answer also saves
    // the same task raced on the shared "<id>.json.tmp" and threw UnauthorizedAccessException
    // on Windows. Many concurrent writers of one record must never throw, and the file must
    // end up as valid JSON for one of the writes.
    [Fact]
    public async Task Concurrent_saves_of_the_same_record_never_throw_and_leave_valid_json()
    {
        var store = NewStore(out var dataPath);
        try
        {
            var writers = Enumerable.Range(0, 200)
                .Select(i => System.Threading.Tasks.Task.Run(() => store.SaveTask(Task(i))));

            await System.Threading.Tasks.Task.WhenAll(writers);   // throws if any writer threw

            var loaded = store.LoadTasks();
            var only = Assert.Single(loaded);
            Assert.Equal("same-id", only.Id);
            Assert.Matches(@"^v\d+$", only.Title);
            Assert.Empty(Directory.GetFiles(Path.Combine(dataPath, "tasks"), "*.tmp"));
        }
        finally { try { Directory.Delete(dataPath, true); } catch { } }
    }

    [Fact]
    public async Task Concurrent_state_saves_never_throw()
    {
        var store = NewStore(out var dataPath);
        try
        {
            var writers = Enumerable.Range(0, 200).Select(i => System.Threading.Tasks.Task.Run(() =>
                store.SaveState(new EmployeeState { Id = "ada", RunsToday = i })));

            await System.Threading.Tasks.Task.WhenAll(writers);

            Assert.Single(store.LoadStates());
        }
        finally { try { Directory.Delete(dataPath, true); } catch { } }
    }
}
