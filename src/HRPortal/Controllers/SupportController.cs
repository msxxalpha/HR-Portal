using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HRPortal.Controllers;

[Authorize]
public class SupportController : Controller
{
    public IActionResult Index() => View();
}
