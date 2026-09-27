using Microsoft.AspNetCore.Mvc;

namespace HRPortal.Controllers;

public class HomeController : Controller
{
    public IActionResult Index() => View();
}