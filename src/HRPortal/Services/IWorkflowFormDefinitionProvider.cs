namespace HRPortal.Services;

/// <summary>
/// Code-first contract for forms that can start a shared workflow.
/// A form remains a normally implemented MVC feature; this contract only
/// publishes its identity and workflow capability to the shared platform.
/// </summary>
public interface IWorkflowFormDefinitionProvider
{
    string Code { get; }
    string Title { get; }
    string Description { get; }
    bool IsActive { get; }
}
