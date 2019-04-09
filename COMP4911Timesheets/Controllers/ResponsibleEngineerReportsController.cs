using System;
using System.Collections;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using COMP4911Timesheets.Data;
using COMP4911Timesheets.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using COMP4911Timesheets.ViewModels;

namespace COMP4911Timesheets.Controllers
{
    [Authorize(Roles = "PM,PA,AD,RE")]
    public class ResponsibleEngineerReportsController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<Employee> _usermanager;

        public ResponsibleEngineerReportsController(ApplicationDbContext context, UserManager<Employee> mgr)
        {
            _context = context;
            _usermanager = mgr;
        }

        // GET: ResponsibleEngineerReports/Reports/6
        public async Task<IActionResult> Reports(int? id)
        {
            var workPackage = await _context.WorkPackages.FindAsync(id);
            var responsibleEngineerReport = _context.ResponsibleEngineerReport.Where(m => m.WorkPackageId == id).ToList();
            workPackage.ResponsibleEngineerReports = responsibleEngineerReport;

            TempData["workPackageName"] = workPackage.Name;
            TempData["workPackageId"] = workPackage.WorkPackageId;
            ViewBag.CreateButton = true;
             
            var project = _context.Projects.Where(m => m.ProjectId == workPackage.ProjectId).FirstOrDefault();
            var parentWorkPackageTest = _context.WorkPackages.Where(m => m.ParentWorkPackageId == workPackage.WorkPackageId && m.Status != WorkPackage.CLOSED).FirstOrDefault();
            if (project.Status != Project.ONGOING || workPackage.Status != WorkPackage.OPENED || parentWorkPackageTest != null) {
                ViewBag.CreateButton = false;
            }
                
            return View(workPackage.ResponsibleEngineerReports);
        }
        
        // ResponsibleEngineerReports/Create/6
        [Authorize(Roles = "AD,RE")]
        public async Task<IActionResult> Create(int? id)
        {
            var workPackage = await _context.WorkPackages.FindAsync(id);
            var project = _context.Projects.Where(m => m.ProjectId == workPackage.ProjectId).FirstOrDefault();

            var manager = _context.ProjectEmployees
                .Where(e => e.ProjectId == project.ProjectId && e.Role == ProjectEmployee.PROJECT_MANAGER)
                .FirstOrDefault();
            if (manager != null) {
                var projectManager = _context.Employees.Where(e => e.Id == manager.EmployeeId).FirstOrDefault();
                TempData["projectManager"] = projectManager.FirstName + " " +  projectManager.LastName;
            }

            var assistant = _context.ProjectEmployees
                .Where(e => e.ProjectId == project.ProjectId && e.Role == ProjectEmployee.PROJECT_ASSISTANT)
                .FirstOrDefault();
            if (assistant != null) {
                var projectManagerAssistant = _context.Employees.Where(e => e.Id == assistant.EmployeeId).FirstOrDefault();
                TempData["projectManagerAssistant"] = projectManagerAssistant.FirstName + " " +  projectManagerAssistant.LastName;
            }
            
            var responsibleEngineer = _context.ProjectEmployees
                .Where(e => e.ProjectId == project.ProjectId && e.Role == ProjectEmployee.RESPONSIBLE_ENGINEER)
                .FirstOrDefault();
            if (responsibleEngineer != null) {
                var projectresponsibleEngineer = _context.Employees.Where(e => e.Id == responsibleEngineer.EmployeeId).FirstOrDefault();
                TempData["projectResponsibleEngineer"] = projectresponsibleEngineer.FirstName + " " +  projectresponsibleEngineer.LastName;
            }

            TempData["projectName"] = project.Name;
            TempData["workPackageName"] = workPackage.Name;
            
            TempData["projectCode"] = project.ProjectCode;
            TempData["workPackageCode"] = workPackage.WorkPackageCode;

            TempData["projectId"] = workPackage.ProjectId;
            TempData["workPackageStatus"] = workPackage.Status;
            
            ResponsibleEngineerReport respEngReport = new ResponsibleEngineerReport
            {
                WeekNumber = Utility.GetWeekNumberByDate(DateTime.Today),
                WorkPackageId = id,
                WorkPackage = workPackage
            };

            var payGrades = await _context.PayGrades.ToListAsync();

            var budgets = await _context.Budgets.Where(u => u.WorkPackageId == workPackage.WorkPackageId).ToListAsync();

            var timeSheets = from ts in _context.Timesheets
                            join tsr in _context.TimesheetRows
                            on ts.TimesheetId equals tsr.TimesheetId into joined
                            from tsr in joined.DefaultIfEmpty()
                            select new {
                                SatHour = tsr.SatHour,
                                SunHour = tsr.SunHour,
                                MonHour = tsr.MonHour,
                                TueHour = tsr.TueHour,
                                WedHour = tsr.WedHour,
                                ThuHour = tsr.ThuHour,
                                FriHour = tsr.FriHour,
                                WeekNumber = ts.WeekNumber,
                                EmployeePayId = ts.EmployeePayId,
                                WorkPackageId = tsr.WorkPackageId
                            };
                            
            var timeSheetsEP = from ts in timeSheets
                            join ep in _context.EmployeePays
                            on ts.EmployeePayId equals ep.EmployeePayId into joined
                            from ep in joined.DefaultIfEmpty()
                            select new {
                                SatHour = ts.SatHour,
                                SunHour = ts.SunHour,
                                MonHour = ts.MonHour,
                                TueHour = ts.TueHour,
                                WedHour = ts.WedHour,
                                ThuHour = ts.ThuHour,
                                FriHour = ts.FriHour,
                                WeekNumber = ts.WeekNumber,
                                EmployeePayId = ts.EmployeePayId,
                                WorkPackageId = ts.WorkPackageId,
                                PayGradeId = ep.PayGradeId
                            };

            //All Weeks
            var timeSheetsAllWeeks = await timeSheetsEP.Where(ts => ts.WorkPackageId == workPackage.WorkPackageId).ToListAsync();
            var timeSheetsAllWeeksDictionary = timeSheetsAllWeeks.ToDictionary(ts => ts.PayGradeId);
            //This Week
            var timeSheetsThisWeek = await timeSheetsEP.Where(ts => ts.WorkPackageId == workPackage.WorkPackageId && ts.WeekNumber == respEngReport.WeekNumber).ToListAsync();
            var timeSheetsThisWeekDictionary = timeSheetsThisWeek.ToDictionary(ts => ts.PayGradeId);
            
            ArrayList pLevels = new ArrayList();
            ArrayList planned = new ArrayList();
            ArrayList spentTD = new ArrayList();
            ArrayList spentTW = new ArrayList();
            ArrayList needed = new ArrayList();

            pLevels.Add("P-Level");
            planned.Add("Planned (Budget Total)");
            spentTD.Add("Spent (To Date)");
            spentTW.Add("Spent (This Week)");
            needed.Add("Needed/Remaining (Input)");

            double[] spentToDate = new double[payGrades.Count];
            double[] spentThisWeek = new double[payGrades.Count];

            foreach(var ts in timeSheetsAllWeeksDictionary) {
                var sum = ts.Value.SatHour +
                          ts.Value.SunHour +
                          ts.Value.MonHour +
                          ts.Value.TueHour +
                          ts.Value.WedHour +
                          ts.Value.ThuHour +
                          ts.Value.FriHour;
                var index = payGrades.IndexOf(payGrades.Find(pg => pg.PayGradeId == ts.Value.PayGradeId));
                spentToDate[index] += sum;
            };

            foreach(var ts in timeSheetsThisWeekDictionary) {
                var sum = ts.Value.SatHour +
                          ts.Value.SunHour +
                          ts.Value.MonHour +
                          ts.Value.TueHour +
                          ts.Value.WedHour +
                          ts.Value.ThuHour +
                          ts.Value.FriHour;
                var index = payGrades.IndexOf(payGrades.Find(pg => pg.PayGradeId == ts.Value.PayGradeId));
                spentThisWeek[index] += sum;
            };

            payGrades.ForEach(pg => {
                pLevels.Add(pg.PayLevel);

                if (budgets == null) {
                    planned.Add("0");  
                } else if (budgets.Any(
                            b => b.PayGradeId == pg.PayGradeId &&
                            b.Status == Budget.VALID &&
                            b.Type == Budget.ESTIMATE)) {
                    planned.Add(budgets.Find(
                            b => b.PayGradeId == pg.PayGradeId &&
                            b.Status == Budget.VALID &&
                            b.Type == Budget.ESTIMATE).REHour.ToString("N"));
                } else {
                    planned.Add("0");
                }
                
                for (int i = 0; i < spentToDate.Length; i++){
                    spentTD.Add(spentToDate[i].ToString("N"));
                }

                for (int i = 0; i < spentThisWeek.Length; i++){
                    spentTW.Add(spentThisWeek[i].ToString("N"));
                }
            });

            ViewBag.tableLength = pLevels.Count;
            ViewBag.pLevels = pLevels;
            ViewBag.planned = planned;
            ViewBag.spentTD = spentTD;
            ViewBag.spentTW = spentTW;
            ViewBag.needed = needed;

            return View(respEngReport);
        }
        
        // POST: ResponsibleEngineerReports/Create
        // To protect from overposting attacks, please enable the specific properties you want to bind to, for 
        // more details see http://go.microsoft.com/fwlink/?LinkId=317598.
        [HttpPost]
        [Authorize(Roles = "AD,RE")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([Bind("WeekNumber,WorkPackageId,WorkPackage,Comments,WorkAccomplished,WorkPlanned,Problem,ProblemAnticipated")] ResponsibleEngineerReport report)
        {
            if (ModelState.IsValid)
            {
                var workPackage = await _context.WorkPackages.FindAsync(report.WorkPackageId);
                var project = _context.Projects.Where(m => m.ProjectId == workPackage.ProjectId).FirstOrDefault();
                var sameWeekReport = _context.ResponsibleEngineerReport
                                    .Where(re => re.WorkPackageId == report.WorkPackageId
                                    && re.WeekNumber == report.WeekNumber).FirstOrDefault();
                
                //Invalid if there has already been a REreport created for this week.
                if (sameWeekReport != null)
                {
                    TempData["ErrorMessage"] = "Responsible Engineer Report already created for this week.";
                    return RedirectToAction("Reports", "ResponsibleEngineerReports", new { id = report.WorkPackageId });
                }
                
                //Invalid if project is closed
                if (project.Status != Project.ONGOING)
                {
                    TempData["ErrorMessage"] = "Cannot create Responsible Engineer Report for closed project.";
                    return RedirectToAction("Reports", "ResponsibleEngineerReports", new { id = report.WorkPackageId });
                }

                //Invalid if work package is closed
                if (workPackage.Status != WorkPackage.OPENED)
                {
                    TempData["ErrorMessage"] = "Cannot create Responsible Engineer Report for closed work packages.";
                    return RedirectToAction("Reports", "ResponsibleEngineerReports", new { id = report.WorkPackageId });
                }

                //Invalid if work package is a parent                
                var parentWorkPackageTest = _context.WorkPackages.Where(m => m.ParentWorkPackageId == workPackage.WorkPackageId && m.Status != WorkPackage.CLOSED).FirstOrDefault();
                if (parentWorkPackageTest != null)
                {
                    TempData["ErrorMessage"] = "Can only create Responsible Engineer Report for leaf work packages.";
                    return RedirectToAction("Reports", "ResponsibleEngineerReports", new { id = report.WorkPackageId });
                }

                report.Status = ResponsibleEngineerReport.VALID;

                _context.Add(report);

                _context.SaveChanges();

                await _context.SaveChangesAsync();

                return RedirectToAction("ReportDetails", "ResponsibleEngineerReports", new { id = report.ResponsibleEngineerReportId });
            }
            return View(report);
        }

        // GET: ResponsibleEngineerReports/ReportDetails/5
        [Authorize(Roles = "PM,PA,AD,RE")]
        public async Task<IActionResult> ReportDetails(int id)
        {
            var responsibleEngineerReport = await _context.ResponsibleEngineerReport.FindAsync(id);
            var workPackage = await _context.WorkPackages.FindAsync(responsibleEngineerReport.WorkPackageId);
            var project = _context.Projects.Where(m => m.ProjectId == workPackage.ProjectId).FirstOrDefault();

            var manager = _context.ProjectEmployees
                .Where(e => e.ProjectId == project.ProjectId && e.Role == ProjectEmployee.PROJECT_MANAGER)
                .FirstOrDefault();
            if (manager != null) {
                var projectManager = _context.Employees.Where(e => e.Id == manager.EmployeeId).FirstOrDefault();
                TempData["projectManager"] = projectManager.FirstName + " " +  projectManager.LastName;
            }

            var assistant = _context.ProjectEmployees
                .Where(e => e.ProjectId == project.ProjectId && e.Role == ProjectEmployee.PROJECT_ASSISTANT)
                .FirstOrDefault();
            if (assistant != null) {
                var projectManagerAssistant = _context.Employees.Where(e => e.Id == assistant.EmployeeId).FirstOrDefault();
                TempData["projectManagerAssistant"] = projectManagerAssistant.FirstName + " " +  projectManagerAssistant.LastName;
            }
            
            var responsibleEngineer = _context.ProjectEmployees
                .Where(e => e.ProjectId == project.ProjectId && e.Role == ProjectEmployee.RESPONSIBLE_ENGINEER)
                .FirstOrDefault();
            if (responsibleEngineer != null) {
                var projectresponsibleEngineer = _context.Employees.Where(e => e.Id == responsibleEngineer.EmployeeId).FirstOrDefault();
                TempData["projectResponsibleEngineer"] = projectresponsibleEngineer.FirstName + " " +  projectresponsibleEngineer.LastName;
            }

            TempData["projectName"] = project.Name;
            TempData["workPackageName"] = workPackage.Name;
            
            TempData["projectCode"] = project.ProjectCode;
            TempData["workPackageCode"] = workPackage.WorkPackageCode;

            TempData["projectId"] = workPackage.ProjectId;
            TempData["workPackageStatus"] = workPackage.Status;

            var payGrades = await _context.PayGrades.ToListAsync();

            var budgets = await _context.Budgets.Where(u => u.WorkPackageId == workPackage.WorkPackageId).ToListAsync();

            var timeSheets = from ts in _context.Timesheets
                            join tsr in _context.TimesheetRows
                            on ts.TimesheetId equals tsr.TimesheetId into joined
                            from tsr in joined.DefaultIfEmpty()
                            select new {
                                SatHour = tsr.SatHour,
                                SunHour = tsr.SunHour,
                                MonHour = tsr.MonHour,
                                TueHour = tsr.TueHour,
                                WedHour = tsr.WedHour,
                                ThuHour = tsr.ThuHour,
                                FriHour = tsr.FriHour,
                                WeekNumber = ts.WeekNumber,
                                EmployeePayId = ts.EmployeePayId,
                                WorkPackageId = tsr.WorkPackageId
                            };
                            
            var timeSheetsEP = from ts in timeSheets
                            join ep in _context.EmployeePays
                            on ts.EmployeePayId equals ep.EmployeePayId into joined
                            from ep in joined.DefaultIfEmpty()
                            select new {
                                SatHour = ts.SatHour,
                                SunHour = ts.SunHour,
                                MonHour = ts.MonHour,
                                TueHour = ts.TueHour,
                                WedHour = ts.WedHour,
                                ThuHour = ts.ThuHour,
                                FriHour = ts.FriHour,
                                WeekNumber = ts.WeekNumber,
                                EmployeePayId = ts.EmployeePayId,
                                WorkPackageId = ts.WorkPackageId,
                                PayGradeId = ep.PayGradeId
                            };

            //All Weeks
            var timeSheetsAllWeeks = await timeSheetsEP.Where(ts => ts.WorkPackageId == workPackage.WorkPackageId).ToListAsync();
            var timeSheetsAllWeeksDictionary = timeSheetsAllWeeks.ToDictionary(ts => ts.PayGradeId);
            //This Week
            var timeSheetsThisWeek = await timeSheetsEP.Where(ts => ts.WorkPackageId == workPackage.WorkPackageId && ts.WeekNumber == responsibleEngineerReport.WeekNumber).ToListAsync();
            var timeSheetsThisWeekDictionary = timeSheetsThisWeek.ToDictionary(ts => ts.PayGradeId);
            
            ArrayList pLevels = new ArrayList();
            ArrayList planned = new ArrayList();
            ArrayList spentTD = new ArrayList();
            ArrayList spentTW = new ArrayList();
            ArrayList needed = new ArrayList();

            pLevels.Add("P-Level");
            planned.Add("Planned (Budget Total)");
            spentTD.Add("Spent (To Date)");
            spentTW.Add("Spent (This Week)");
            needed.Add("Needed/Remaining (Input)");

            double[] spentToDate = new double[payGrades.Count];
            double[] spentThisWeek = new double[payGrades.Count];

            foreach(var ts in timeSheetsAllWeeksDictionary) {
                var sum = ts.Value.SatHour +
                          ts.Value.SunHour +
                          ts.Value.MonHour +
                          ts.Value.TueHour +
                          ts.Value.WedHour +
                          ts.Value.ThuHour +
                          ts.Value.FriHour;
                var index = payGrades.IndexOf(payGrades.Find(pg => pg.PayGradeId == ts.Value.PayGradeId));
                spentToDate[index] += sum;
            };

            foreach(var ts in timeSheetsThisWeekDictionary) {
                var sum = ts.Value.SatHour +
                          ts.Value.SunHour +
                          ts.Value.MonHour +
                          ts.Value.TueHour +
                          ts.Value.WedHour +
                          ts.Value.ThuHour +
                          ts.Value.FriHour;
                var index = payGrades.IndexOf(payGrades.Find(pg => pg.PayGradeId == ts.Value.PayGradeId));
                spentThisWeek[index] += sum;
            };

            payGrades.ForEach(pg => {
                pLevels.Add(pg.PayLevel);

                if (budgets == null) {
                    planned.Add("0");  
                } else if (budgets.Any(
                            b => b.PayGradeId == pg.PayGradeId &&
                            b.Status == Budget.VALID &&
                            b.Type == Budget.ESTIMATE)) {
                    planned.Add(budgets.Find(
                            b => b.PayGradeId == pg.PayGradeId &&
                            b.Status == Budget.VALID &&
                            b.Type == Budget.ESTIMATE).REHour.ToString("N"));
                } else {
                    planned.Add("0");
                }
                
                for (int i = 0; i < spentToDate.Length; i++){
                    spentTD.Add(spentToDate[i].ToString("N"));
                }

                for (int i = 0; i < spentThisWeek.Length; i++){
                    spentTW.Add(spentThisWeek[i].ToString("N"));
                }
            });

            ViewBag.tableLength = pLevels.Count;
            ViewBag.pLevels = pLevels;
            ViewBag.planned = planned;
            ViewBag.spentTD = spentTD;
            ViewBag.spentTW = spentTW;
            ViewBag.needed = needed;
            
            return View(responsibleEngineerReport);
        }

        private bool ProjectExists(int id)
        {
            return _context.Projects.Any(e => e.ProjectId == id);
        }
    }
}
