using HRPortal.Services;

using Node = HRPortal.Services.MonthlyPerformanceScopeResolver.Node;
using Person = HRPortal.Services.MonthlyPerformanceScopeResolver.Person;
using Rank = HRPortal.Services.MonthlyPerformanceScopeResolver.Rank;
using Job = HRPortal.Services.MonthlyPerformanceScopeResolver.Job;

var nodes = new List<Node>
{
    new(1, null, "مدیرعامل", 1, "1"),
    new(10, 1, "مدیریت", 100, "100,105"), // 105 is a same-position colleague
    new(20, 1, "مدیریت", 200, "200"),     // sibling manager, never a subordinate
    new(11, 10, "کارشناس", null, "101,102"), // two employees in one child node
    new(12, 10, "ریاست", 300, "300"),
    new(13, 12, "کارشناس", null, "301"),
    new(14, 10, "کارشناس", null, null), // personnel HR section assignment only
    new(21, 20, "کارشناس", null, "201"),
    new(22, 20, "مدیریت", 250, "250"), // a same-level, different branch
    new(15, 10, "کارشناس", null, "999"), // inactive person
    new(23, 99, "کارشناس", null, "600"), // broken parent: no authority given
    new(30, 1, "مدیریت", 700, "700"),
    new(31, 30, "کارشناس", null, "701،702") // Persian comma support
};
var people = new List<Person>
{
    new(1, "فعال", 1, null, null, null, null, "مدیرعامل"),
    new(100, "فعال", null, null, 10, null, null, "مدیر"),
    new(101, "فعال", null, null, 20, 20, 3, "کارشناس"), // stale HR department, direct node wins
    new(102, "فعال", null, null, null, null, null, "کارشناس"),
    new(103, "فعال", null, null, 10, null, 3, "کارشناس"), // no direct position; under holder
    new(104, "فعال", null, 14, 10, null, null, "کارشناس"),
    new(105, "فعال", null, null, 10, null, null, "مدیر"), // explicit same-node occupant
    new(110, "فعال", null, null, 10, null, null, "کارشناس"),
    new(200, "فعال", null, null, 20, null, null, "مدیر"),
    new(201, "فعال", null, null, 20, null, null, "کارشناس"),
    new(250, "فعال", null, null, 20, null, null, "مدیر"),
    new(300, "فعال", null, null, 10, null, null, "رئیس"),
    new(301, "فعال", null, null, null, null, null, "کارشناس"),
    new(450, "فعال", 11, null, null, null, null, "کارشناس"), // direct via Employee.PositionId
    new(600, "فعال", null, null, null, null, null, "کارشناس"),
    new(700, "فعال", null, null, null, null, null, "مدیر"),
    new(701, "فعال", null, null, null, null, null, "کارشناس"),
    new(702, "فعال", null, null, null, null, null, "کارشناس"),
    new(999, "غیرفعال", null, null, null, null, null, "کارشناس"),
    new(400, "فعال", null, null, null, null, null, "کارشناس")
};
var ranks = new List<Rank> {new(1, "مدیریت"),new(2, "ریاست"),new(3, "کارشناس")};
var jobs = new List<Job> {new("کارشناس فروش", "کارشناس"),new("مدیر", "مدیریت")};
List<int> Scoped(int evaluatorId, IReadOnlyCollection<Node>? customNodes = null) =>
    MonthlyPerformanceScopeResolver.Resolve(evaluatorId, customNodes ?? nodes, people, ranks, jobs)
        .Select(s => s.EmployeeId).OrderBy(i => i).ToList();
void Verify(int evaluator, params int[] expected)
{
    var actual = Scoped(evaluator);
    if (!actual.SequenceEqual(expected.OrderBy(i => i)))
        throw new Exception($"Evaluator {evaluator} expected [{string.Join(",", expected)}], got [{string.Join(",", actual)}]");
}

Verify(100, 101,102,103,104,110,300,301,450);
Verify(200,201,250);
Verify(300,301);
Verify(700,701,702);
Verify(101); // an ordinary employee in a shared specialist node has no subordinates
Verify(105, 101,102,104,300,301,450); // may cover descendant nodes, never same-node staff or peers
Verify(400);
Verify(1,100,101,102,103,104,105,110,200,201,250,300,301,450,700,701,702);

// A same-node HR department member is not a peer of the explicitly designated
// position holder; if no holder is declared, do not infer authority over them.
var withoutHolder = nodes.Select(n => n.Id == 10 ? n with { PositionHolderEmployeeId = null } : n).ToList();
var noHolderScope = Scoped(100, withoutHolder);
if (noHolderScope.Contains(103) || noHolderScope.Contains(110))
    throw new Exception("Same-node members must not be evaluated without an explicit position holder.");
if (!noHolderScope.Contains(101) || !noHolderScope.Contains(102))
    throw new Exception("Employees explicitly assigned to descendant nodes were unexpectedly removed.");

// Verify independent rank resolution: personnel profile rank takes precedence over
// the node's own title, so co-located specialists are not evaluated as managers.
var staff = MonthlyPerformanceScopeResolver.Resolve(100,nodes,people,ranks,jobs);
var a = staff.Single(x => x.EmployeeId == 101);
if (a.RankTitle != "کارشناس" || a.NodeId != 11)
    throw new Exception("Wrong performance rank/placement for specialist with stale HR fields.");
if (staff.Count != staff.Select(x => x.EmployeeId).Distinct().Count())
    throw new Exception("Employee was duplicated in the evaluator list.");

// A revised organizational structure retains codes but changes node numeric IDs.
// HR employee Unit/Department/Section fields may still refer to old revision IDs.
var previousCodes = new Dictionary<int, string>
{
    [910] = "ORG-MANAGER-A",
    [920] = "ORG-MANAGER-B",
    [930] = "ORG-RETIRED"
};
var currentCodes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
{
    ["ORG-MANAGER-A"] = 10,
    ["ORG-MANAGER-B"] = 20
};
if (MonthlyPerformanceScopeResolver.CurrentNodeId(910, previousCodes, currentCodes) != 10 ||
    MonthlyPerformanceScopeResolver.CurrentNodeId(920, previousCodes, currentCodes) != 20 ||
    MonthlyPerformanceScopeResolver.CurrentNodeId(930, previousCodes, currentCodes) is not null ||
    MonthlyPerformanceScopeResolver.CurrentNodeId(9999, previousCodes, currentCodes) is not null)
    throw new Exception("Previous-revision organization IDs were not safely mapped by stable code.");

var employeeWithOldDepartment = people.Select(p =>
    p.Id == 103 ? p with { DepartmentId = 910 } : p).ToList();
var reconciledPeople = employeeWithOldDepartment.Select(p =>
    p with { DepartmentId = p.DepartmentId == 910
        ? MonthlyPerformanceScopeResolver.CurrentNodeId(p.DepartmentId, previousCodes, currentCodes)
        : p.DepartmentId }).ToList();
if (!MonthlyPerformanceScopeResolver.Resolve(100,nodes,reconciledPeople,ranks,jobs)
    .Any(x => x.EmployeeId == 103))
    throw new Exception("Employee with a cloned-structure department reference is missing from evaluator scope.");

Console.WriteLine("PASS: Strict hierarchy, two staff in one child node, department-only membership,");
Console.WriteLine("      no sibling/peer access, nested descendants, stale profile precedence,");
Console.WriteLine("      inactive staff, invalid branches, Persian CSV delimiters, and no duplicates.");
