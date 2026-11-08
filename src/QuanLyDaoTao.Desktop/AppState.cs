using QuanLyDaoTao.Core;

namespace QuanLyDaoTao.Desktop;

public sealed class AppState
{
    public TrainingRepository Repository { get; }
    public IReadOnlyList<Employee> Employees { get; private set; } = [];
    public IReadOnlyList<TrainingActivity> Activities { get; private set; } = [];
    public IReadOnlyList<TrainingSupplement> Supplements { get; private set; } = [];
    public event EventHandler? Changed;
    public AppState(TrainingRepository repository) { Repository = repository; Reload(); }
    public void Reload()
    {
        Employees = Repository.GetEmployees();
        Activities = Repository.GetActivities();
        Supplements = Repository.GetSupplements();
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
