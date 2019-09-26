Deleted some content by Aslam

Deleted some content by Aslam
using System.Net.Http;
using BackOfficeServer.ViewModels;
using Newtonsoft.Json.Linq;
using static Atlas.Common.Utils.BackOfficeEnum;
using System.IO;
using ICSharpCode.SharpZipLib.Zip;
using System.Xml.Serialization;
using WebIntegration;
using BackOfficeServer.BackOfficeAccountServer;
using System.Text;
using System.Net;
using System.Net.Http.Headers;
using BackOfficeServer.Integrations;
using System.Linq.Dynamic;
using Atlas.Domain.Model.Account;
using DevExpress.Snap;
using DevExpress.Snap.Core.API;
using FrameworkLibrary.ResponseBase;
using FrameworkLibrary.ExceptionBase;
using FrameworkLibrary.Common;
using BackOfficeServer.OrchestrationService;
using Atlas.Domain.Model.Branch;
using System.Configuration;
using Atlas.Domain.Model.CreditLife;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using Atlas.Domain.Model.BOS_NuCardClientMap;
using BackOfficeServer.Models;
using Atlas.ThirdParty.XDSConnect;
using Atlas.Domain.Model.ThirdParty;
using System.Text.RegularExpressions;
using Atlas.Enumerators;
using Atlas.ThirdParty.DebiCheck.Services;
using Atlas.ThirdParty.DebiCheck.Models;
using Atlas.Domain.DTO.DebiCheck;
using System.Threading.Tasks;
using System.Globalization;
using Atlas.Domain.Model.Bank;
using Atlas.Domain.Model.ABSA;
using static BackOfficeServer.Common.ApplicationCaseUtil;
using Atlas.Common.Interface;
using System.Web.SessionState;
using System.Xml;
// using Atlas.Online.Data.Models.Definitions;

namespace BackOfficeServer.Controllers
{
    public class ApplicationController : BaseController
    {
        private static readonly ILog log = LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);
        private static int fpVerificationCount = 0;

        /// <summary>
        /// It will return account details against specific application
        /// </summary>
        /// <param name="id"></param>
        /// <returns>application details</returns>
        ///         
        [HttpGet]
        public Response<VMApplicationDetails> GetApplicationById(int id)
        {
            
            var application = new VMApplicationDetails();
            var lstDocument = new List<DocumentsDto>();
            string dbtReferenceNumber = string.Empty;
            try
            {
                log.Info(string.Format("Get application details by id: {0}", id));
                var role = this.HasAccess(BO_Object.Applications, BackOfficeEnum.Action.VIEW);
                if (role.Status == Constants.Success)
                {
                    DocumentsDto mDocDto = new DocumentsDto();
                    ApplicationDto info = new ApplicationDto();
                    using (var result = new BackOfficeWebServiceClient("BackOfficeWebServer.NET"))
                    {
                        var filterConditions = FilterUtil.GetFiltersAsString(BO_Object.Applications, Convert.ToInt32(HttpContext.Current.Session["UserId"]));
                        info = result.GetApplicationDetail(filterConditions, id);


                        var balanceResponse = fn_BalanceNuCard(id, info.Disbursement.NuCardNumber);
                        if (balanceResponse.Status == Constants.Success)
                        {
                            var jsonObj = balanceResponse.Data;
                            var bal = jsonObj.methodResponse.@params.param.value.@struct.member.Where(a => a.name == "balanceAmount").Select(a => a.value.@int).FirstOrDefault();
                            info.Disbursement.NuCardBalance = bal;
                        }

                        NUC_NuCard nuCardData = null;

                        using (var uow = new UnitOfWork())
                        {
                            var nuCardStatusId = new XPQuery<NUC_NuCard>(uow)?.FirstOrDefault(a => a.SequenceNum == info.Disbursement.NuCardNumber)?.Status?.NuCardStatusId;
                            if (nuCardStatusId != null)
                            {
                                var data = new XPQuery<NUC_NuCardStatus>(uow)?.Where(nuCard => nuCard.NuCardStatusId == nuCardStatusId)?.FirstOrDefault();
                                AutoMapper.Mapper.CreateMap<NUC_NuCardStatus, Atlas.Domain.DTO.Nucard.NUC_NuCardStatusDTO>();
                                info.Disbursement.NuCardStatus = Mapper.Map<NUC_NuCardStatus, Atlas.Domain.DTO.Nucard.NUC_NuCardStatusDTO>(data);
                            }
                            var nuCardDetails = new XPQuery<BOS_NuCardClientMap>(uow).Where(a => a.NationalId == info.ApplicationClient.IDNumber).FirstOrDefault();
                            if (nuCardDetails != null)
                            {
                                nuCardData = new XPQuery<NUC_NuCard>(uow).Where(a => a.NuCardId == nuCardDetails.NuCardId && a.Status.NuCardStatusId == (int)Atlas.Enumerators.NuCard.NuCardStatus.ISSUE && a.Blocked == false).FirstOrDefault();
                            }
                        }
                        //info.Disbursement.NuCardNumber = nuCardData?.SequenceNum;
                        //info.Disbursement.cardNumber = nuCardData?.CardNum;
                        info.CardInfo = new CardInfo()
                        {

                            VoucherNumber = info.Disbursement.NuCardNumber,
                            CardNumber = info.Disbursement.cardNumber,
                            AllocateNuCard = CheckToAllocateNewCard((int)info.ClientId, info.ApplicationClient.IDNumber),
                            AllocateNuCardReason = CheckToAllocateNewCard((int)info.ClientId, info.ApplicationClient.IDNumber) ? "New" : string.Empty
                        };


                        if (info.Quotation.LoanAmount != 0)
                        {
                            SalaryFrequency frequency = (SalaryFrequency)info.Employer.SalaryType;
                            info.PaymentFrequency = frequency.ToString();
                            info.RateOfIntrest = info.Quotation.InterestRate * 12;
                        }
                        try
                        {
                            log.Info(string.Format("get documents for customer {0}", id));
                            lstDocument = result.GetFiles(0, info.ApplicationId, (HttpContext.Current.Request.Url.Authority + HttpContext.Current.Request.ApplicationPath)).ToList();
                            info.document = lstDocument;

                        }
                        catch (Exception ex)
                        {
                            log.Error(string.Format("Error getting documents for customer {0}\nError: {1}", id, ex));
                            throw;
                        }

                        if (info.ApplicationId <= 0)
                        {
                            log.Info(string.Format("Application details not found for id: {0}", id));
                            return Response<VMApplicationDetails>.CreateResponse(Constants.Failed, null, new ErrorHandler { ErrorCode = Constants.ApplicationErrorCode, Message = Constants.ApplicationDetailsNotFound });
                        }
                        else
                        {
                            try
                            {
                                info.ApplicationClient.HistoryUrl = Helper.GetHistoryUrl(BO_Object.Applications, BO_ObjectAPI.client, id);
                                if (info.BankDetail != null)
                                    info.BankDetail.HistoryUrl = Helper.GetHistoryUrl(BO_Object.Applications, BO_ObjectAPI.bankdetails, id);
                                if (info.Employer != null)
                                    info.Employer.HistoryUrl = Helper.GetHistoryUrl(BO_Object.Applications, BO_ObjectAPI.employerdetails, id);
                                info.Affordability.HistoryUrl = Helper.GetHistoryUrl(BO_Object.Applications, BO_ObjectAPI.affordability, id);
                                info.Quotation.HistoryUrl = Helper.GetHistoryUrl(BO_Object.Applications, BO_ObjectAPI.quotation, id);
                                info.Disbursement.HistoryUrl = Helper.GetHistoryUrl(BO_Object.Applications, BO_ObjectAPI.disbursement, id);
                                info.RateOfIntrest = info.Quotation.InterestRate * 12;
                            }
                            catch (Exception ex)
                            {
                                log.Error(string.Format("Error getting interest rate for application: {0}\nError: {1}", id, ex));
                                throw;
                            }

                            // commented and added by Aslam
                            //string bankStatus = string.Empty, employerStatus = string.Empty, incomeStatus = string.Empty, clientStatus = string.Empty, applicationStatus = string.Empty, affordailityStatus = string.Empty, disbursementStatus = string.Empty, quotationStatus = string.Empty;
                            string bankStatus = string.Empty, employerStatus = string.Empty, incomeStatus = string.Empty, clientStatus = string.Empty, applicationStatus = string.Empty, affordailityStatus = string.Empty, disbursementStatus = string.Empty, quotationStatus = string.Empty, documentStatus = string.Empty;
                            // till here

                            var actionsBank = new List<BOS_ActionDTO>();
                            var actionsEmployer = new List<BOS_ActionDTO>();
                            var actionIncomeExpenese = new List<BOS_ActionDTO>();
                            var actionsProfile = new List<BOS_ActionDTO>();
                            var actionsApplications = new List<BOS_ActionDTO>();
                            var actionsMaster = new List<BOS_ActionDTO>();
                            var actionsAffordability = new List<BOS_ActionDTO>();
                            var actionDisbursement = new List<BOS_ActionDTO>();
                            var actionQuotation = new List<BOS_ActionDTO>();
                            //Added by Aslam
                            var actionDocument = new List<BOS_ActionDTO>();
                            //till here

                            var allSubmitted = false;
                            List<BOS_ApplicationChecklistDto> _checkList = GetCheckList(id);
                            using (var web = new BackOfficeWebServiceClient("BackOfficeWebServer.NET"))
                            {
                                using (var uow = new UnitOfWork())
                                {
                                    try
                                    {
                                        log.Info("Getting status of each category of application.");
                                        var statusobject = web.GetApplicationCategoryStatus(info.ApplicationId);
                                        allSubmitted = statusobject.Where(o => o.applicationObject != BO_ObjectAPI.documents && o.applicationObject != BO_ObjectAPI.master).All(o => o.NewStatusId == (int)NewStatus.SUBMITTED);

                                        log.Info("getting actions for each category of application");
                                        foreach (var item in statusobject)
                                        {
                                            try
                                            {
                                                var _status = new XPQuery<BOS_Status>(uow).Where(s => s.StatusId == item.NewStatusId).Select(s => s.Description).FirstOrDefault();
                                                if (item.applicationObject == BO_ObjectAPI.client)
                                                {
                                                    log.Info("Getting client actions");
                                                    clientStatus = _status;
                                                    actionsProfile = ActionUtil.GetAllowedActions(BO_Object.Applications, BO_ObjectAPI.client, item.NewStatusId, id);
                                                }

                                                //Added by Aslam
                                                if (item.applicationObject == BO_ObjectAPI.documents)
                                                {
                                                    log.Info("Getting Document actions");
                                                    documentStatus = _status;
                                                    actionDocument = ActionUtil.GetAllowedActions(BO_Object.Applications, BO_ObjectAPI.documents, item.NewStatusId, id);
                                                }
                                                //Till here

                                                else if (item.applicationObject == BO_ObjectAPI.bankdetails)
                                                {
                                                    log.Info("Getting bankdetails actions");
                                                    bankStatus = _status;
                                                    actionsBank = ActionUtil.GetAllowedActions(BO_Object.Applications, BO_ObjectAPI.bankdetails, item.NewStatusId, id);
                                                    var _list = _checkList.Where(x => x.ChecklistStatus.ChecklistStatusId == 2).ToList();
                                                    foreach (var remPers in _list)
                                                    {
                                                        string _act = remPers.ApplicationChecklistMaster.Check;
                                                        BOS_ActionDTO itemToRemove = new BOS_ActionDTO();
                                                        switch (_act)
                                                        {
                                                            case "AVSCheck":
                                                                itemToRemove = actionsBank.Find(x => x.ActionId == (int)BackOfficeEnum.Action.AVS_CHECK);

                                                                break;
                                                            case "CDVCheck":
                                                                itemToRemove = actionsBank.Find(x => x.ActionId == (int)BackOfficeEnum.Action.CHECK_DIGIT);

                                                                break;

                                                        }

                                                        actionsBank.Remove(itemToRemove);
                                                    }
                                                }
                                                else if (item.applicationObject == BO_ObjectAPI.employerdetails)
                                                {
                                                    log.Info("Getting employer actions");
                                                    employerStatus = _status;
                                                    actionsEmployer = ActionUtil.GetAllowedActions(BO_Object.Applications, BO_ObjectAPI.employerdetails, item.NewStatusId, id);

                                                    var isEmployeeCompleteCheckStatus = _checkList.Where(c => c.ApplicationChecklistMaster.Check == "EmployerDetailsComplete")?.FirstOrDefault()?.ChecklistStatus.ChecklistStatusId;

                                                    if (isEmployeeCompleteCheckStatus != null && isEmployeeCompleteCheckStatus == 1)
                                                    {
                                                        var actionsToRemove = actionsEmployer.Find(x => x.ActionId == (int)BackOfficeEnum.Action.VERIFY);
                                                        actionsEmployer.Remove(actionsToRemove);
                                                    }

                                                    var isEmployeeVerificationCheckStatus = _checkList.Where(c => c.ApplicationChecklistMaster.Check == "EmployerVerificationCheck")?.FirstOrDefault()?.ChecklistStatus.ChecklistStatusId;

                                                    if (isEmployeeVerificationCheckStatus != null && isEmployeeVerificationCheckStatus == 2)
                                                    {
                                                        var actionsToRemove = actionsEmployer.Find(x => x.ActionId == (int)BackOfficeEnum.Action.VERIFY);
                                                        actionsEmployer.Remove(actionsToRemove);
                                                    }
                                                }
                                                else if (item.applicationObject == BO_ObjectAPI.application)
                                                {
                                                    log.Info("Getting application actions");
                                                    applicationStatus = _status;
                                                }
                                                else if (allSubmitted && item.applicationObject == BO_ObjectAPI.master)
                                                {
                                                    log.Info("Getting master actions");
                                                    actionsMaster = ActionUtil.GetAllowedActions(BO_Object.Applications, BO_ObjectAPI.master, item.NewStatusId, id);
                                                }
                                                else if (item.applicationObject == BO_ObjectAPI.affordability)
                                                {
                                                    log.Info("Getting affordabiity actions");
                                                    affordailityStatus = _status;
                                                    actionsAffordability = ActionUtil.GetAllowedActions(BO_Object.Applications, BO_ObjectAPI.affordability, item.NewStatusId, id);
                                                }
                                                else if (item.applicationObject == BO_ObjectAPI.disbursement)
                                                {
                                                    log.Info("Getting disbursement actions");
                                                    disbursementStatus = _status;
                                                    actionDisbursement = ActionUtil.GetAllowedActions(BO_Object.Applications, BO_ObjectAPI.disbursement, item.NewStatusId, id);

                                                    if (actionDisbursement != null && actionDisbursement.Count() > 0)
                                                    {
                                                        BOS_ActionDTO actionsToRemove = null;


                                                        //check if new card is to be allocated and accordingly manage actions
                                                        // Temporarily commented by Aslam
                                                        //if (info.CardInfo.AllocateNuCard || !string.IsNullOrEmpty(info.Disbursement.CardDetails?.NewVocherNumber) && info.Disbursement.NuCardNumber != info.Disbursement.CardDetails?.NewVocherNumber)
                                                        //{
                                                        //    //check if new card account is already created, if yes then remove action to create nucard debit order
                                                        //    var nuCardAccount = info.LoanAccount.FirstOrDefault(x => x.AccountType.Description.ToUpper() == "NUCARD");
                                                        //    if (nuCardAccount != null)
                                                        //    {
                                                        //        var debitOrder = new XPQuery<ACC_DebitOrder>(uow).FirstOrDefault(d => d.AccountId == nuCardAccount.AccountId);

                                                        //        if (debitOrder != null)
                                                        //        {
                                                        //            actionsToRemove = actionDisbursement.Find(x => x.ActionId == (int)BackOfficeEnum.Action.NUCARD_SWIPE);
                                                        //        }
                                                        //        else
                                                        //        {
                                                        //            actionsToRemove = actionDisbursement.Find(x => x.ActionId == (int)BackOfficeEnum.Action.SWIPE);
                                                        //        }
                                                        //    }
                                                        //    else
                                                        //    {
                                                        //        actionsToRemove = actionDisbursement.Find(x => x.ActionId == (int)BackOfficeEnum.Action.SWIPE);
                                                        //    }
                                                        //}
                                                        //else
                                                        //{
                                                        //    actionsToRemove = actionDisbursement.Find(x => x.ActionId == (int)BackOfficeEnum.Action.NUCARD_SWIPE);
                                                        //}

                                                        actionDisbursement.Remove(actionsToRemove);
                                                    }
                                                }
                                                else if (item.applicationObject == BO_ObjectAPI.quotation)
                                                {
                                                    log.Info("Getting quotation actions");
                                                    quotationStatus = _status;
                                                    bool isVerified = web.IsCheckVerified(BO_Object.Applications, id, "PhoneNumberCheck");
                                                    if (isVerified)
                                                    {
                                                        actionQuotation = ActionUtil.GetAllowedActions(BO_Object.Applications, BO_ObjectAPI.quotation, item.NewStatusId, id);
                                                    }
                                                }
                                            }
                                            catch (Exception ex)
                                            {
                                                log.Error(string.Format("Error getting actions for application {0}\nError: {1}", id, ex));
                                                throw;
                                            }
                                        }

                                        if (string.IsNullOrEmpty(bankStatus))
                                            actionsBank = ActionUtil.GetAllowedActions(BO_Object.Applications, BO_ObjectAPI.bankdetails, 0, id);
                                        if (string.IsNullOrEmpty(employerStatus))
                                            actionsEmployer = ActionUtil.GetAllowedActions(BO_Object.Applications, BO_ObjectAPI.employerdetails, 0, id);
                                        if (string.IsNullOrEmpty(clientStatus))
                                            actionsProfile = ActionUtil.GetAllowedActions(BO_Object.Applications, BO_ObjectAPI.client, 0, id);
                                        if (string.IsNullOrEmpty(affordailityStatus))
                                            actionsAffordability = ActionUtil.GetAllowedActions(BO_Object.Applications, BO_ObjectAPI.affordability, 0, id);
                                        if (string.IsNullOrEmpty(disbursementStatus))
                                            actionDisbursement = ActionUtil.GetAllowedActions(BO_Object.Applications, BO_ObjectAPI.disbursement, 0, id);
                                        if (string.IsNullOrEmpty(quotationStatus))
                                            actionQuotation = ActionUtil.GetAllowedActions(BO_Object.Applications, BO_ObjectAPI.quotation, 0, id);
                                        //Added by Aslam
                                        if (string.IsNullOrEmpty(documentStatus))
                                            actionDocument = ActionUtil.GetAllowedActions(BO_Object.Applications, BO_ObjectAPI.documents, 0, id);
                                        //till here

                                        //if(string.IsNullOrEmpty(quotationStatus) && quotationStatus == BackOfficeEnum.NewStatus.STATUS_QUOTATION_GENERATED.ToString())
                                        //{
                                        //    GetMultiplePdf(id);
                                        //}
                                    }
                                    catch (Exception ex)
                                    {
                                        log.Error(string.Format("Erro getting status of application {0}]nError: {1}", id, ex));
                                        throw;
                                    }
                                }

                                try
                                {
                                    log.Info("Assigning actions to each category");
                                    if (info.Employer != null)
                                        info.Employer.Actions = actionsEmployer;
                                    if (info.BankDetail != null)
                                        info.BankDetail.Actions = actionsBank;
                                    if (info.ApplicationClient != null)
                                        info.ApplicationClient.Actions = actionsProfile;
                                    if (info.Affordability != null)
                                        info.Affordability.Actions = actionsAffordability;
                                    if (info.Disbursement != null)
                                        info.Disbursement.Actions = actionDisbursement;
                                    if (info.Quotation != null)
                                        info.Quotation.Actions = actionQuotation;

                                    // Added by Aslam
                                    if (info.document != null)
                                    {
                                        //mDocDto.Actions = actionDocument;
                                        info.Document = mDocDto;
                                        info.Document.Actions = actionDocument;
                                    }
                                    // till here

                                    info.Actions = actionsApplications;
                                }
                                catch (Exception ex)
                                {
                                    log.Error(string.Format("Error assigning actions for application {0}\nError: {1}", id, ex));
                                    throw;
                                }

                                using (var uow = new UnitOfWork())
                                {
                                    try
                                    {
                                        var dbtOrder = new XPQuery<Atlas.Domain.Model.Account.ACC_DebitOrder>(uow).Where(a => a.AccountId == info.AccountId).FirstOrDefault();
                                        if (dbtOrder != null)
                                            dbtReferenceNumber = dbtOrder.transactionID;
                                        info.DBTOrderTransactionId = dbtReferenceNumber;
                                    }
                                    catch (Exception ex)
                                    {
                                        log.Error(string.Format("Error getting debit order for application {0}\nError: {1}", id, ex));
                                        throw;
                                    }
                                }
                            }

                            ACC_AccountTypeDTO acctype = new ACC_AccountTypeDTO();
                            using (var uow = new UnitOfWork())
                            {
                                try
                                {
                                    var accAccount = new XPQuery<ACC_AccountType>(uow).FirstOrDefault(a => a.AccountTypeId == 1);
                                    acctype = AutoMapper.Mapper.Map<ACC_AccountType, ACC_AccountTypeDTO>(accAccount);
                                }
                                catch (Exception ex)
                                {
                                    log.Error(string.Format("Error getting account type details for application {0}\nError: {1}", id, ex));
                                    throw;
                                }
                            }

                            try
                            {
                                application = (new VMApplicationDetails()
                                {

                                    ApplicationId = info.ApplicationId,
                                    ApplicationDetail = info,
                                    Actions = actionsMaster,
                                    EventHistory = GetApplicationEventHistory(info.ApplicationId),
                                    CheckList = GetCheckList(id),
                                    Validation = acctype
                                });
                            }
                            catch (Exception ex)
                            {
                                log.Error(string.Format("Error getting application details for application {0}\nError: {1}", id, ex));
                                return Response<VMApplicationDetails>.CreateResponse(Constants.FailedCode, null, new ErrorHandler() { ErrorCode = Constants.ApplicationErrorCode, Message = Constants.ApplicationDetailsErrorMessage });
                            }
                        }
                    }
                    return Response<VMApplicationDetails>.CreateResponse(Constants.Success, application, null);
                }
                else
                {
                    log.Info(role.Error);
                    return Response<VMApplicationDetails>.CreateResponse(Constants.Failed, null, role.Error);
                }
            }
            catch (Exception ex)
            {
                log.Error(string.Format("Error getting application details for application {0}\nError: {1}", id, ex));
                return Response<VMApplicationDetails>.CreateResponse(Constants.Failed, null,
                    new ErrorHandler
                    {
                        ErrorCode = Constants.ApplicationErrorCode,
                        Message = Constants.ApplicationDetailsErrorMessage
                    });
            }
        }

        private List<BOS_ApplicationChecklistDto> GetCheckList(int applicationId)
        {
            using (var web = new BackOfficeWebServiceClient("BackOfficeWebServer.NET"))
            {
                try
                {
                    log.Info("getting application checklist");
                    var checklist = web.GetApplicationChecklist(applicationId)?.ToList();
                    if (checklist != null)
                        checklist.ForEach(h => h.ApplicationChecklistMaster.DisplayName = Helper.GetCategoryDisplayName((BO_ObjectAPI)Enum.Parse(typeof(BO_ObjectAPI), h.ApplicationChecklistMaster.SubObject, true)).Replace("Master", "Application"));
                    return checklist;
                }
                catch (Exception ex)
                {
                    log.Error(string.Format("Error getting application checklist\nError: {0}", ex));
                    throw;
                }
            }
        }

        /// <summary>
        /// It will take clientId and application Data object
        /// It will save the data in backofficeWeb service
        /// </summary>
        /// <param name="id"></param>
        /// <param name="obj"></param>
        /// <returns>new application</returns>
        [HttpPost]
        [Route("Customers/{ClientId}/Application/New")]
        public Response<ApplicationDto> CreateApplication(int clientId, [FromBody] VMCustomerDetails client)
        {
            try
            {
                CustomersController ctrlCustomer = new CustomersController();
                using (var result = new BackOfficeWebServiceClient("BackOfficeWebServer.NET"))
                {
                    var IdNumber = client.CategoryList[0].Profile.Client.IDNumber;
                    clientId = Convert.ToInt32(result.GetClientByIDNumId(IdNumber));
                }
                if (clientId < 1)
                {
                    log.Info(string.Format("Validating date of birth : {1} for new customer : {0}", client.CategoryList[0].Profile.Client.IDNumber, client.CategoryList[0].Profile.Client.DateOfBirth));
                    IDValidator isvalid = new IDValidator(client.CategoryList[0].Profile.Client.IDNumber);
                    client.CategoryList[0].Profile.Client.DateOfBirth = isvalid.isValid() == true ? Convert.ToDateTime(isvalid.GetDateOfBirth()) : default(DateTime);
                    var IsFemale = new IDValidator(client.CategoryList[0].Profile.Client.IDNumber).IsFemale();
                    client.CategoryList[0].Profile.Client.Gender = (IsFemale == true ? 'F' : 'M');
                    log.Info(string.Format("Creating new customer {0}", client.CategoryList[0].Profile.Client.IDNumber));
                    var createCustomer = CreateCustomer(client);
                    clientId = Convert.ToInt32(createCustomer);

                    log.Info(string.Format("Updating bank details {0}", client.CategoryList[0].Profile.Client.IDNumber));
                    var bankDto = new Atlas.Online.Data.Models.DTO.BankDetailDto();
                    var bankDetails = ctrlCustomer.AddClientBankDetails(bankDto, clientId, 1);
                }
                if (clientId > 0)
                {
                    var cust = ctrlCustomer.GetCustomerById(clientId);
                    if (cust.Status == Constants.Success)
                    {
                        if (cust.Data.CategoryList[0].Employer != null)
                        {
                            int salaryType = client.CategoryList[0].Employer.SalaryType;
                            int payDay = client.CategoryList[0].Employer.PayDay;

                            client.CategoryList[0].Employer = cust.Data.CategoryList[0].Employer;
                            client.CategoryList[0].Employer.SalaryType = salaryType;
                            client.CategoryList[0].Employer.PayDay = payDay;
                        }
                    }

                    log.Info(string.Format("Updating employer details {0}", client.CategoryList[0].Profile.Client.IDNumber));
                    var employer = ctrlCustomer.AddUpdateEmployer(Convert.ToInt32(clientId), client.CategoryList[0].Employer, 1);
                    client.LoanReason = client.LoanReason == null ? new LoanReasonDto() { ReasonId = 0, Description = "Not Set" } : client.LoanReason;
                    ApplicationDto obj = new ApplicationDto()
                    {
                        Amount = 0,
                        Period = 0,
                        RepaymentDate = DateTime.Now,
                        Reason = client.LoanReason,
                        ScoreSharpResult=client.ScoreSharpResult

                    };
                    log.Info(string.Format("Creating New Application for client {0}", clientId));
                    var role = this.HasAccess(BO_Object.Applications, BackOfficeEnum.Action.NEW);
                    if (role.Status == Constants.Success)
                    {
                        using (UnitOfWork uow = new UnitOfWork())
                        {
                            try
                            {
                                var filterConditions = FilterUtil.GetFiltersAsString(BO_Object.Applications, Convert.ToInt32(HttpContext.Current.Session["UserId"]));

                                try
                                {
                                    var svcWorkflow = new XPQuery<BOS_WorkFlowService>(uow).Where(x => x.TransitionFunction == "/applications/new/customers/{CLIENT_ID}").FirstOrDefault();



                                    //if (Helper.AreRulesMet(0, svcWorkflow.WorkFlowServiceId, 2, out string ruleName, true, clientId))
                                    if (true)
                                    {
                                        List<Dictionary<string, int>> defaultStatusList = ObjectStateUtil.GetdefaultStatusList(BO_Object.Applications);
                                        using (var result = new BackOfficeWebServiceClient("BackOfficeWebServer.NET"))
                                        {
                                            int branchId = Convert.ToInt32(HttpContext.Current.Session["BranchId"]);
                                            var res = result.CreateApplication(filterConditions, obj, clientId, defaultStatusList.ToArray(), branchId);

                                            var rateofInterest = DBManager.GetInterestRate(res.ApplicationId, DateTime.Now);
                                            rateofInterest = rateofInterest / 12;

                                            if (res != null && res.ApplicationId > 0)
                                            {
                                                result.CreateApplicationChecklist(res.ApplicationId);
                                                result.UpdateApplicationBankCheckStatus(res.ApplicationId);
                                                result.UpdateApplicationEmployerCheckStatus(res.ApplicationId);
                                                result.UpdateApplicationProfileCheckStatus(res.ApplicationId);
                                                res.Quotation.InterestRate = result.updateInterestRate(rateofInterest, res.ApplicationId);

                                                NUC_NuCard nuCardData = null;
                                                var nuCardDetails = new XPQuery<BOS_NuCardClientMap>(uow).Where(a => a.NationalId == res.ApplicationClient.IDNumber)?.OrderByDescending(n => n.AllocationDate).FirstOrDefault();
                                                if (nuCardDetails != null)
                                                {
                                                    nuCardData = new XPQuery<NUC_NuCard>(uow).Where(a => a.NuCardId == nuCardDetails.NuCardId).FirstOrDefault();// && a.Status.NuCardStatusId == (int)Atlas.Enumerators.NuCard.NuCardStatus.ISSUE && a.Blocked == false).FirstOrDefault();

                                                    if (nuCardData != null)
                                                        res.Disbursement.NuCardNumber = nuCardData.SequenceNum;
                                                }


                                                if (res.Disbursement.NuCardNumber != null)
                                                {
                                                    var updatedDisbursementData = UpdateTrackingNum(res.Disbursement.NuCardNumber, res.Disbursement.DisbursementId);
                                                    if (updatedDisbursementData != null)
                                                    {
                                                        res.Disbursement = updatedDisbursementData;
                                                        if (!string.IsNullOrEmpty(res.Disbursement.cardNumber))
                                                            res.Disbursement.cardNumber = ClipperCrypto.Decrypt(res.Disbursement.cardNumber);
                                                    }

                                                }

                                                ViewModels.VMApplications vm = new ViewModels.VMApplications();
                                                vm.Comment = "Application Created Successfully";
                                                var data = JsonConvert.SerializeObject(vm);
                                                UpdateApplicationEventHistory(res.ApplicationId, BackOfficeEnum.Action.NEW.ToString(), data, null, BO_ObjectAPI.master);
                                                log.Info("Application Created Successfully");
                                                return Response<ApplicationDto>.CreateResponse(Constants.Success, res, null);
                                            }
                                            else
                                            {
                                                log.Info(string.Format("Creating Application By Client Id Failed"));
                                                return Response<ApplicationDto>.CreateResponse(Constants.Failed, null, new ErrorHandler { ErrorCode = Constants.ApplicationErrorCode, Message = Constants.ApplicationCreateErrorMessage });
                                            }
                                        }
                                    }
                                    else
                                    {
                                        //log.Info(string.Format("Pre-conditions not met. {0} is pending", ruleName));
                                        //return Response<ApplicationDto>.CreateResponse(Constants.Failed, null, new ErrorHandler() { ErrorCode = Constants.ApplicationErrorCode, Message = string.Format(Constants.PreConditionsNotMet, ruleName) });
                                    }
                                }
                                catch (Exception ex)
                                {
                                    log.Error("Error getting worklow\nError: {0}", ex);
                                    throw;
                                }
                            }
                            catch (Exception ex)
                            {
                                log.Error("Error getting filters\nError: {0}", ex);
                                throw;
                            }
                        }
                    }
                    else
                    {
                        log.Error(string.Format("Authetication Failed\n{0}", role.Error));
                        return Response<ApplicationDto>.CreateResponse(Constants.Failed, null, role.Error);
                    }
                }
                else
                {
                    log.Error(string.Format("Customer not created"));
                    return Response<ApplicationDto>.CreateResponse(Constants.Failed, null, new ErrorHandler { ErrorCode = Constants.ApplicationErrorCode, Message = Constants.ApplicationCreateErrorMessage });
                }
            }
            catch (Exception ex)
            {
                log.Error(string.Format("Error creating new application\nError: {0}", ex));
                return Response<ApplicationDto>.CreateResponse(Constants.Failed, null, new ErrorHandler { ErrorCode = Constants.ApplicationErrorCode, Message = Constants.ApplicationCreateErrorMessage });
            }
        }

        [NonAction]
        /// <summary>
        /// Gets application event history
        /// </summary>
        /// <param name="applicationId"></param>
        /// <returns>list of event history</returns>
        private List<BOS_ApplicationEventHistoryDTO> GetApplicationEventHistory(int applicationId)
        {
            log.Info(string.Format("Getting event history for application {0}", applicationId));
            try
            {
                List<BOS_ApplicationEventHistoryDTO> applicationHistory = new List<BOS_ApplicationEventHistoryDTO>();
                using (var uow = new UnitOfWork())
                {
                    var history = new XPQuery<BOS_ApplicationEventHistory>(uow).Where(a => a.ApplicationId == Convert.ToInt64(applicationId)).OrderByDescending(a => a.ActionDate).ToList();
                    Mapper.CreateMap<BOS_ApplicationEventHistory, BOS_ApplicationEventHistoryDTO>();
                    foreach (var temp in history)
                    {
                        applicationHistory.Add(Mapper.Map<BOS_ApplicationEventHistory, BOS_ApplicationEventHistoryDTO>(temp));
                    }
                    applicationHistory.ForEach(h => h.DisplayName = Helper.GetCategoryDisplayName((BO_ObjectAPI)Enum.Parse(typeof(BO_ObjectAPI), h.Category, true)).Replace("Master", "Application"));
                }
                return applicationHistory;
            }
            catch (Exception ex)
            {
                log.Info(string.Format("Error getting application event history. ApplicationId {0}\nError", applicationId, ex.ToString()));
                throw;
            }
        }

        /// <summary>
        /// Gets list of actions on specific application based on last status
        /// </summary>
        /// <param name="id"></param>
        /// <returns>application actions</returns>
        [HttpGet]
        [Route("Applications/{id}/Actions")]
        public Response<VMApplicationActionList> Action(int id)
        {
            log.Info(string.Format("Getting all actions for application {0}", id));
            List<VMApplicationActionList> actionList = new List<VMApplicationActionList>();
            try
            {
                var role = this.HasAccess(BO_Object.Applications, BackOfficeEnum.Action.VIEW);
                if (role.Status == Constants.Success)
                {
                    using (UnitOfWork uow = new UnitOfWork())
                    {
                        var filterConditions = FilterUtil.GetFiltersAsString(BO_Object.Applications, Convert.ToInt32(HttpContext.Current.Session["UserId"]));
                        var actions = ActionUtil.GetAllowedActions(BO_Object.Applications, id, filterConditions);
                        actionList.Add(new VMApplicationActionList { ApplicationId = id, Actions = actions });
                    }
                    return Response<VMApplicationActionList>.CreateResponse(Constants.Success, actionList[0], null);
                }
                else
                {
                    log.Info(string.Format("Authentication Error: {0}", role.Error));
                    return Response<VMApplicationActionList>.CreateResponse(Constants.Failed, null, role.Error);
                }
            }

            catch (Exception ex)
            {
                log.Error(string.Format("Error getting actions for application {0}\nError: {1}", id, ex));
                return Response<VMApplicationActionList>.CreateResponse(Constants.Failed, null, new ErrorHandler() { ErrorCode = Constants.ApplicationErrorCode, Message = Constants.ActionsErrorMessage });
            }


        }

        [NonAction]
        public Response<string> EditApplicationClient(int applicationId, [FromBody]  Atlas.Online.Data.Models.DTO.VMApplications applicationClient)
        {
            string editedFields = string.Empty;
            ErrorHandler error = new ErrorHandler();
            try
            {
                log.Info(string.Format("Edit applicationClientId {0}", applicationId));
                var role = this.HasAccess(BO_Object.Customers, BackOfficeEnum.Action.EDIT);
                if (role.Status == Constants.Success)
                {
                    using (var uow = new UnitOfWork())
                    {
                        if (applicationClient != null)
                        {
                            using (var result = new BackOfficeWebServiceClient("BackOfficeWebServer.NET"))
                            {
                                var filterConditions = FilterUtil.GetFiltersAsString(BO_Object.Customers, Convert.ToInt32(HttpContext.Current.Session["UserId"]));
                                editedFields = result.EditApplicationClient(applicationId, applicationClient, filterConditions, out error);
                            }

                            if (editedFields == null)
                            {
                                log.Info(string.Format("Edit client {0} failed. Error: {1}", applicationId, error));
                                return Response<string>.CreateResponse(Constants.Failed, null, error);
                            }
                            else
                            {
                                if (editedFields == Constants.AuthorizationFailedCode)
                                {
                                    log.Info(string.Format("Edit applicationClientId {0} failed. Error: {1}", applicationId, error));
                                    return Response<string>.CreateResponse(Constants.Failed, null, error);
                                }
                                else
                                {
                                    log.Info(string.Format("Edit applicationClientId {0} success", applicationId));
                                    return Response<string>.CreateResponse(Constants.Success, editedFields, null);
                                }
                            }

                        }
                    }
                    log.Info(string.Format("Edit applicationClientId {0} failed. Payload is null", applicationId));
                    return Response<string>.CreateResponse(Constants.Failed, null, new ErrorHandler() { ErrorCode = Constants.CustomerErrorCode, Message = Constants.ProvideCustomer });
                }
                else
                {
                    log.Info(string.Format("Edit applicationClientId {0} failed. Authentication failed {1}", applicationId, role.Error));
                    return Response<string>.CreateResponse(Constants.Failed, null, role.Error);
                }
            }
            catch (Exception ex)
            {
                log.Error(string.Format("Error editing applicationClientId {0}\nError: {1}", applicationId, ex));
                return Response<string>.CreateResponse(Constants.Failed, null, new ErrorHandler() { ErrorCode = Constants.CustomerErrorCode, Message = Constants.UpdateClientErrorMessage });
            }
        }

        [HttpPost]
        [Route("Applications/bankdetails/{applicationId}")]
        public Response<string> UpdateApplicationBankDetails(BankDetailDto bankDetail, int applicationId, int newStatusId)
        {

            try
            {
                log.Info(string.Format("updating bank details for application {0}", applicationId));
                using (var uow = new UnitOfWork())
                {
                    var role = this.HasAccess(BO_Object.Applications, BackOfficeEnum.Action.EDIT);
                    if (role.Status == Constants.Success)
                    {
                        var filterConditions = FilterUtil.GetFiltersAsString(BO_Object.BankDetails, Convert.ToInt32(HttpContext.Current.Session["UserId"]));
                        using (var result = new BackOfficeWebServiceClient("BackOfficeWebServer.NET"))
                        {
                            string editedFields = result.UpdateApplicationBankInfo(filterConditions, bankDetail, applicationId, newStatusId);
                            if (!string.IsNullOrEmpty(editedFields))
                            {
                                log.Info(string.Format("Bank details updated for application {0}", applicationId));
                                return Response<string>.CreateResponse(Constants.Success, editedFields, null);
                            }
                            else
                            {
                                log.Error(string.Format("Bank details not found for application {0}", applicationId));
                                return Response<string>.CreateResponse(Constants.Failed, editedFields, new ErrorHandler() { ErrorCode = Constants.ApplicationErrorCode, Message = Constants.BankDetailsNotFoundMessage });
                            }
                        }
                    }
                    else
                    {
                        log.Info(string.Format("Authentication failed\nError: {0}", role.Error));
                        return Response<string>.CreateResponse(Constants.Failed, null, role.Error);
                    }
                }
            }
            catch (Exception ex)
            {
                log.Error(string.Format("error updating bank details for application {0}\nError: {1}", applicationId, ex.ToString()));
                return Response<string>.CreateResponse(Constants.Failed, null, new ErrorHandler() { ErrorCode = Constants.ApplicationErrorCode, Message = Constants.UpdateBankDetailsErrorMessage });
            }
        }

        [HttpPost]
        [Route("Applications/{id}/edit/employerdetails")]
        public Response<string> AddUpdateApplicationEmployer(int id, EmployerDTO emp, int newstatusId)
        {
            log.Info(string.Format("Adding/Updaing employer details for application {0}", id));
            try
            {
                if (emp != null)
                {
                    string editedFields = string.Empty;
                    using (var uow = new UnitOfWork())
                    {
                        using (var result = new BackOfficeWebServiceClient("BackOfficeWebServer.NET"))
                        {
                            var filterConditions = FilterUtil.GetFiltersAsString(BO_Object.EmployerDetails, Convert.ToInt32(HttpContext.Current.Session["UserId"]));
                            if (emp.EmployerId > 0)
                            {
                                try
                                {
                                    log.Info(string.Format("Updaing employer details for application {0}", id));
                                    //update employer detailss
                                    editedFields = result.UpdateApplicationEmployer(filterConditions, emp, id);
                                }
                                catch (Exception ex)
                                {
                                    log.Error(string.Format("Error updating employer details for application {0}\nError: {1}", id, ex));
                                    throw;
                                }
                            }
                            else
                            {
                                try
                                {
                                    log.Info(string.Format("Adding employer details for application {0}", id));
                                    //add employer details                                  
                                    result.AddApplicationEmployer(filterConditions, emp, id, newstatusId);
                                }
                                catch (Exception ex)
                                {
                                    log.Error(string.Format("Error adding employer details for application {0}\nError: {1}", id, ex));
                                    throw;
                                }
                            }
                            log.Info(string.Format("Employer added/updated successfully for application {0}", id));
                            return Response<string>.CreateResponse(Constants.Success, editedFields, null);
                        }
                    }
                }
                else
                {
                    log.Info("Employer details not found");
                    return Response<string>.CreateResponse(Constants.Failed, null, new ErrorHandler() { ErrorCode = Constants.ApplicationErrorCode, Message = Constants.EmployerDetailsNotFoundMessage });
                }
            }
            catch (Exception ex)
            {
                log.Info(string.Format("Error adding/updating employer details {0}\nError: {1}", id, ex));
                return Response<string>.CreateResponse(Constants.Failed, null, new ErrorHandler() { ErrorCode = Constants.ApplicationErrorCode, Message = Constants.UpdateEmployerErrorMessage });
            }
        }

        public void UpdateApplicationEmployerDetails(long id, int newStatus, string action, string data, string editedFields, BO_ObjectAPI category)
        {
            try
            {
                try
                {
                    log.Info("get edited fields");
                    if (!string.IsNullOrEmpty(editedFields) && editedFields != "[]")
                    {
                        if (!editedFields.Contains("Status"))
                        {
                            editedFields = getEditedFields(editedFields, (int)id, newStatus, BO_ObjectAPI.employerdetails);
                        }
                    }
                    else
                    {
                        var lstEditedFields = new List<VMEditedFields>();
                        lstEditedFields.Add(new VMEditedFields() { FieldName = "Status", OldValue = null, NewValue = "EDITED" });
                        editedFields = JsonConvert.SerializeObject(lstEditedFields);
                    }
                }
                catch (Exception ex)
                {
                    log.Error(string.Format("Error in get edited fields \nError: {1}", id, ex));
                    throw;
                }

                using (var uow = new UnitOfWork())
                {
                    using (var result = new BackOfficeWebServiceClient("BackOfficeWebServer.NET"))
                    {
                        bool res = false;
                        int clientid = Convert.ToInt32(id);
                        var filterConditions = FilterUtil.GetFiltersAsString(BO_Object.EmployerDetails, Convert.ToInt32(HttpContext.Current.Session["UserId"]));
                        res = result.ChangeApplicationEmployerStatus(filterConditions, clientid, newStatus);
                        if (res)
                        {
                            try
                            {
                                UpdateApplicationEventHistory(id, action, data, editedFields, category);
                            }
                            catch (Exception ex)
                            {
                                log.Error(string.Format("error updating application event history\nError: {0}", ex));
                                throw;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                log.Info(string.Format("Error updating employer details\nError: {0}", ex.ToString()));
                throw;
            }
        }

        [HttpPost]
        [Route("applications/GetScoreCard/{id}")]
        public Response<object> fn_GetScoreCard(int id)
        {
            log.Info(string.Format("get score card for application {0}", id));
            try
            {
                var applicationDetails = new VMApplicationDetails();
                CompuScan.transReplyClass replyClass = new CompuScan.transReplyClass();
                ApplicationDto application = new ApplicationDto();
                var filterConditions = FilterUtil.GetFiltersAsString(BO_Object.Applications, Convert.ToInt32(HttpContext.Current.Session["UserId"]));

                using (var result = new BackOfficeWebServiceClient("BackOfficeWebServer.NET"))
                {
                    try
                    {
                        application = result.GetApplicationDetail(filterConditions, id);
                    }
                    catch (Exception ex)
                    {
                        log.Info(string.Format("Error getting application details for score card\nError: {0}", ex));
                        throw;
                    }
                }
                if (application != null)
                {
                    System.Xml.XmlDocument doc;
                    GenerateXMLDoc(application, out doc, out StringBuilder xmldoc);
                    string userName = string.Empty;
                    string Password = string.Empty;
                    using (var uow = new UnitOfWork())
                    {
                        var scoreConfig = new XPQuery<ThirdParty_Config>(uow).Where(config => config.ThirdParty.ThirdPartyId == (int)BackOfficeEnum.ThirdPartyIntegration.Credit_Score && config.Branch.BranchId == 1/*Convert.ToInt64(HttpContext.Current.Session["BranchId"])*/).FirstOrDefault();

                        if (scoreConfig != null)
                        {
                            userName = scoreConfig.UserId;
                            Password = scoreConfig.Password;
                        }
                    }

                    try
                    {
                        CompuScan.NormalEnqRequestParamsType normalEnqRequestParamsType = new CompuScan.NormalEnqRequestParamsType()
                        {
                            pUsrnme = userName,
                            pPasswrd = Password,
                            pInput_Format = "XML",
                            pOrigin = "SOAPUI",
                            pOrigin_Version = "4.5.2",
                            pVersion = "1.0",
                            pTransaction = Convert.ToString(xmldoc)
                        };

                        replyClass = new Application_Integrations().fn_GetScoreCard(normalEnqRequestParamsType);

                        try
                        {
                            byte[] file = Convert.FromBase64String(replyClass.retData);
                            File.WriteAllBytes(HttpContext.Current.Server.MapPath("~/Content/") + "Dummy.zip", file);
                        }
                        catch (Exception ex)
                        {
                            log.Info(string.Format("error generating file for score card\nError: {0}", ex));
                            throw;
                        }
                    }
                    catch (Exception ex)
                    {
                        log.Info(string.Format("error generating enq request params for score card\nError: {0}", ex));
                        throw;
                    }
                    applicationDetails = GenerateZipFile(id, doc);

                    using (var result = new BackOfficeWebServiceClient("BackOfficeWebServer.NET"))
                    {
                        try
                        {
                            result.UpdateApplicationCheckRuleStatus(id, "CreditScoreCheck", null);
                        }
                        catch (Exception ex)
                        {
                            log.Info(string.Format("error updating credit score check status in application checklist\nError: {0}", ex));
                            throw;
                        }
                    }
                    log.Info("Get score card success");
                    return Response<object>.CreateResponse(Constants.Success, applicationDetails, null);
                }
                else
                {
                    log.Info(string.Format("Application {0} details not found while getting score card", id));
                    return Response<object>.CreateResponse(Constants.Failed, applicationDetails, new ErrorHandler() { ErrorCode = Constants.ApplicationErrorCode, Message = Constants.ApplicationDetailsNotFound });
                }
            }
            catch (Exception ex)
            {
                log.Error(string.Format("Error getting score card for application {0}\nErro: {1}", id, ex.ToString()));
                return Response<object>.CreateResponse(Constants.Failed, null, new ErrorHandler() { ErrorCode = Constants.ApplicationErrorCode, Message = Constants.ScoreCardErrorMessage });
            }
        }

        private VMApplicationDetails GenerateZipFile(int id, System.Xml.XmlDocument doc)
        {
            log.Info("Generate zip file");
            VMApplicationDetails applicationDetails;
            var xmlDoc = new System.Xml.XmlDocument();
            var importFile = new FileInfo(HttpContext.Current.Server.MapPath("~/Content/Dummy.zip"));
            using (var zipStream = new ZipInputStream(importFile.OpenRead()))
            {
                try
                {
                    ZipEntry theEntry;
                    while ((theEntry = zipStream.GetNextEntry()) != null)
                    {
                        var lowerName = theEntry.Name.ToLower();
                        if (lowerName.EndsWith(".xml") && !lowerName.StartsWith("__macosx"))
                        {
                            doc.Load(zipStream);
                        }
                    }
                    string jsonText = JsonConvert.SerializeXmlNode(doc);
                    XmlSerializer serializer = new XmlSerializer(typeof(ROOT), new XmlRootAttribute("ROOT"));
                    StringReader stringReader = new StringReader(doc.InnerXml);
                    var Data = (ROOT)serializer.Deserialize(stringReader);
                    using (var result = new BackOfficeWebServiceClient("BackOfficeWebServer.NET"))
                    {
                        var cpa_installment = (Data.EnqCC_NLR_SUMMARY.Summary.CCA_MonthlyInstallment == null ? 0 : Convert.ToInt32(Data.EnqCC_NLR_SUMMARY.Summary.CCA_MonthlyInstallment));
                        var nlr_installment = (Data.EnqCC_NLR_SUMMARY.Summary.NLR_MonthlyInstallment == null ? 0 : Convert.ToInt32(Data.EnqCC_NLR_SUMMARY.Summary.NLR_MonthlyInstallment));
                        var affordability = result.UpdateAffordabilityCompuScore(id, Convert.ToInt32(Data.EnqCC_CompuSCORE.ROW[0].SCORE), Convert.ToInt32(Data.Enquiry_ID), Convert.ToDateTime(Data.EnqCC_SRCHCRITERIA.ROW[0].ENQ_DATE), Convert.ToInt32(cpa_installment), Convert.ToInt32(nlr_installment));
                        var jsonResponse = JsonConvert.SerializeObject(Data);
                        var saveResponse = result.SaveCreditScore(id, jsonText);
                    }
                    var appData = GetApplicationById(id);
                    applicationDetails = appData.Data;
                    File.Delete(HttpContext.Current.Server.MapPath("~/Content/") + "Dummy.zip");
                }
                catch (Exception ex)
                {
                    log.Info(string.Format("error generating zip stream for score card\nError: {0}", ex));
                    throw;
                }
            }

            return applicationDetails;
        }

        private static void GenerateXMLDoc(ApplicationDto application, out System.Xml.XmlDocument doc, out StringBuilder xmldoc)
        {
            log.Info("Generate XML document");
            doc = new System.Xml.XmlDocument();
            xmldoc = new System.Text.StringBuilder();
            var isSA = new IDValidator(application.ApplicationClient.IDNumber).isValid();
            try
            {
                xmldoc.Append("<Transactions><Search_Criteria>");
                xmldoc.Append("<CS_Data>Y</CS_Data>");
                xmldoc.Append("<CPA_Plus_NLR_Data>Y</CPA_Plus_NLR_Data>");
                xmldoc.Append("<Deeds_Data>N</Deeds_Data>");
                xmldoc.Append("<Directors_Data>N</Directors_Data>");
                xmldoc.Append("<Identity_number>" + application.ApplicationClient.IDNumber + "</Identity_number>");
                xmldoc.Append("<Surname>" + application.ApplicationClient.Surname + "</Surname>");
                xmldoc.Append("<Forename>" + application.ApplicationClient.Firstname + "</Forename>");
                xmldoc.Append("<Forename2></Forename2>");
                xmldoc.Append("<Forename3></Forename3>");
                xmldoc.Append("<Gender>" + application.ApplicationClient.Gender + "</Gender>");

                xmldoc.Append("<Passport_flag>" + (isSA == true ? "N" : "Y") + "</Passport_flag>");
                xmldoc.Append("<DateOfBirth>" + Convert.ToDateTime(application.ApplicationClient.DateOfBirth).ToString("yyyyMMdd") + "</DateOfBirth>");
                xmldoc.Append("<Address1>" + application.ResidentialAddress.AddressLine1 + "</Address1>");
                xmldoc.Append("<Address2>" + application.ResidentialAddress.AddressLine2 + "</Address2>");
                xmldoc.Append("<Address3>" + application.ResidentialAddress.AddressLine3 + "</Address3>");
                xmldoc.Append("<Address4>" + application.ResidentialAddress.AddressLine4 + "</Address4>");
                xmldoc.Append("<PostalCode>" + (application.ResidentialAddress.PostalCode.Length > 5 ? application.ResidentialAddress.PostalCode.Substring(0, 5) : application.ResidentialAddress.PostalCode) + "</PostalCode>");
                xmldoc.Append("<HomeTelCode></HomeTelCode>");
                xmldoc.Append("<HomeTelNo></HomeTelNo>");
                xmldoc.Append("<WorkTelCode></WorkTelCode>");
                xmldoc.Append("<WorkTelNo>" + application.Employer.TelephoneNo + "</WorkTelNo>");
                xmldoc.Append("<CellTelNo>" + application.ApplicationClient.CellNo + "</CellTelNo>");
                xmldoc.Append("<ResultType>XML</ResultType>");
                xmldoc.Append("<RunCodix>Y</RunCodix>");
                xmldoc.Append("<CodixParams>");
                xmldoc.Append("<PARAMS>");
                xmldoc.Append("<PARAM_NAME>IncomePM</PARAM_NAME>");
                xmldoc.Append("<PARAM_VALUE>" + 0 + "</PARAM_VALUE>");
                xmldoc.Append("</PARAMS>");
                xmldoc.Append("</CodixParams>");
                xmldoc.Append("<Adrs_Mandatory>N</Adrs_Mandatory>");
                xmldoc.Append("<Enq_Purpose>12</Enq_Purpose>");
                xmldoc.Append("<Run_CompuScore>Y</Run_CompuScore>");
                xmldoc.Append("<ClientConsent>Y</ClientConsent>");
                xmldoc.Append("</Search_Criteria></Transactions>");
                xmldoc = HandleSpecialCharacters(xmldoc.ToString());
            }
            catch (Exception ex)
            {
                log.Info(string.Format("error generating XML for score card\nError: {0}", ex));
                throw;
            }
        }

        private static StringBuilder HandleSpecialCharacters(string xmldoc)
        {
            var rawxmldoc = xmldoc.ToString();
            rawxmldoc = rawxmldoc.Replace("&", "&amp;").Replace("'", "&apos;").Replace("`", "&apos;");
            return new StringBuilder(rawxmldoc);
        }

        [NonAction]
        public Response<CardSwipe.HandShakeRsp> fn_Handshake()
        {
            try
            {
                log.Info("Execute handshake");
                var serviceResult = new Application_Integrations().fn_Handshake();
                if (serviceResult.status == "00")
                    return Response<CardSwipe.HandShakeRsp>.CreateResponse(Constants.Success, serviceResult, null);
                else
                    return Response<CardSwipe.HandShakeRsp>.CreateResponse(Constants.Failed, null, new ErrorHandler() { ErrorCode = serviceResult.status, Message = EnumCheck.Description((Enum_Handshake)Convert.ToInt32(serviceResult.status)) });
            }
            catch (Exception ex)
            {
                log.Error(string.Format("Error executing handshake\nError: {0}", ex));
                return Response<CardSwipe.HandShakeRsp>.CreateResponse(Constants.Failed, null, new ErrorHandler() { ErrorCode = Constants.ApplicationErrorCode, Message = Constants.HandshakeExecutionErrorMessage });
            }
        }

        [NonAction]
        public Response<CardSwipe.tranIDResp> fn_tranIDQuery(string data)
        {
            try
            {
                log.Info("Execute tran id query");
                var request = JsonConvert.DeserializeObject<Integrations.SwipeCard>(data);
                var serviceResult = new BackOfficeServer.Integrations.Application_Integrations().fn_tranIDQuery(request);
                if (serviceResult.responseCode == "00")
                    return Response<CardSwipe.tranIDResp>.CreateResponse(Constants.Success, serviceResult, null);
                else
                    return Response<CardSwipe.tranIDResp>.CreateResponse(Constants.Failed, null, new ErrorHandler() { ErrorCode = serviceResult.responseCode, Message = EnumCheck.Description((Enum_TranIDQuery)Convert.ToInt32(serviceResult.responseCode)) });
            }
            catch (Exception ex)
            {
                log.Error(string.Format("error executing function tranIDQuery\nError: {0}", ex));
                return Response<CardSwipe.tranIDResp>.CreateResponse(Constants.Failed, null, new ErrorHandler() { ErrorCode = Constants.ApplicationErrorCode, Message = Constants.FunctionExecutionErrorMessage });
            }
        }

        [NonAction]
        public Response<SignFlowAPIHelperClasses.SigningCeremonyResponse> fn_PerformSignFlow(int id, string data)
        {
            SignFlowAPIHelperClasses.SigningCeremonyResponse result = null;
            try
            {
                log.Info("Execute sign flow");
                string quotationPath = "~/Content/CustomerAcceptanceContracts/_Quota";
                string documentPath = "~/Content/SignedDocuments/";
                string signPath = "~/Content/";
                ApplicationDto application = new ApplicationDto();
                SignRequest request = JsonConvert.DeserializeObject<Integrations.SignRequest>(data);

                using (var service = new BackOfficeWebServiceClient("BackOfficeWebServer.NET"))
                {
                    try
                    {
                        var filterConditions = FilterUtil.GetFiltersAsString(BO_Object.Applications, Convert.ToInt32(HttpContext.Current.Session["UserId"]));
                        application = service.GetApplicationDetail(filterConditions, id);
                        if (application != null)
                        {
                            request.SignerEmail = application.ApplicationClient.Email;
                            request.SignerFullName = application.ApplicationClient.Firstname + " " + application.ApplicationClient.Surname;
                            request.SignerIndentificationNumber = application.ApplicationClient.IDNumber;
                            request.SignerLocation = application.ResidentialAddress.AddressLine3;
                            request.SignerMobileNumber = application.ApplicationClient.CellNo;
                        }
                    }
                    catch (Exception ex)
                    {
                        log.Info(string.Format("Error getting application {0} details while executing fn_PerformSignFlow\nError: {1}", id, ex));
                        throw;
                    }
                }

                if (!File.Exists(HttpContext.Current.Server.MapPath(quotationPath + id + "_Client.pdf")))
                    return Response<SignFlowAPIHelperClasses.SigningCeremonyResponse>.CreateResponse(Constants.Failed, null, new ErrorHandler() { ErrorCode = Constants.ApplicationErrorCode, Message = Constants.FileNotFound });


                string docName = id.ToString() + "_" + request.SignerIndentificationNumber.ToString() + ".pdf";
                byte[] readBytes = File.ReadAllBytes(HttpContext.Current.Server.MapPath(quotationPath + id + "_Client.pdf"));
                request.DocName = docName;
                request.DocBytes = readBytes;
                request.SignBytes = File.ReadAllBytes(HttpContext.Current.Server.MapPath(signPath + "BlankSign.png"));
                request.SignaturePage = 1;
                request.SignerReason = "Quotation Submission";
                request.SignerTrustOrigin = "FlowFinance";

                VMOTP json = JsonConvert.DeserializeObject<VMOTP>(data);
                request.SignerTrustReference = Convert.ToString(
                "OTP:" + application.Quotation.OTPReferenceNumber + ", " +
                "IP_Address:" + json.ClientIPAddress + ", " +
                "User_Agent:" + json.ClientUserAgent + ", " +
                "Browser_Agent:" + json.ClientBrowserAgent);

                try
                {
                    result = new Application_Integrations().fn_PerformSignFlow(request);
                    byte[] bytes = Convert.FromBase64String(result.SignedDocumentField);

                    bool exists = Directory.Exists(HttpContext.Current.Server.MapPath(documentPath));

                    if (!exists)
                        Directory.CreateDirectory(HttpContext.Current.Server.MapPath(documentPath));

                    File.WriteAllBytes(HttpContext.Current.Server.MapPath(documentPath + docName), bytes);
                    return Response<SignFlowAPIHelperClasses.SigningCeremonyResponse>.CreateResponse(Constants.Success, result, null);
                }
                catch (Exception ex)
                {
                    if (ConfigurationManager.AppSettings["SignFlowBypass"] == "true")
                    {
                        log.Info("Mock SignFlowBypass is true. Mocking response.");
                        log.Error(string.Format("Error executing fn_PerformSignFlow for application: {0}\nException: {1}", id, ex));

                        return Response<SignFlowAPIHelperClasses.SigningCeremonyResponse>.CreateResponse(Constants.Success, result, null);
                    }

                    log.ErrorFormat(string.Format("Error executing fn_PerformSignFlow for application {0}\nError: {1}", id, ex));
                    throw;
                }
            }
            catch (Exception ex)
            {
                log.ErrorFormat(string.Format("Error executing fn_PerformSignFlow for application {0}\nError: {1}", id, ex));
                return Response<SignFlowAPIHelperClasses.SigningCeremonyResponse>.CreateResponse(Constants.Failed, null, new ErrorHandler() { ErrorCode = Constants.ApplicationErrorCode, Message = Constants.SignFlowExecutionErrorMessage });
            }
        }

        [NonAction]
        public Response<CardSwipe.BinCheck> fn_PerformCardSwipe()
        {
            try
            {
                log.Info("Executing card swipe");
                var serviceResult = new Application_Integrations().fn_PerformCardSwipe();
                if (serviceResult.ResponseCode == "00")
                    return Response<CardSwipe.BinCheck>.CreateResponse(Constants.Success, serviceResult, null);
                else
                    return Response<CardSwipe.BinCheck>.CreateResponse(Constants.Failed, null, new ErrorHandler() { ErrorCode = serviceResult.ResponseCode, Message = serviceResult.ResponseCode });
            }
            catch (Exception ex)
            {
                log.Info(string.Format("error executing card swipe\nError: {0}", ex.ToString()));
                return Response<CardSwipe.BinCheck>.CreateResponse(Constants.Failed, null, new ErrorHandler() { ErrorCode = Constants.ApplicationErrorCode, Message = Constants.CardSwipeExecutionErrorMessage });
            }
        }

        [NonAction]
        public Response<CardSwipe.AuthRsp> fn_CreateDebitOrder(int id)
        {
            try
            {
                log.Info(string.Format("Create debit order for application {0}", id));
                var applicationDto = new ApplicationDto();
                using (var uow = new UnitOfWork())
                {
                    using (var result = new BackOfficeWebServiceClient("BackOfficeWebServer.NET"))
                    {
                        try
                        {
                            var appFilterConditions = FilterUtil.GetFiltersAsString(BO_Object.Applications, Convert.ToInt32(HttpContext.Current.Session["UserId"]));
                            applicationDto = result.GetApplicationDetail(appFilterConditions, id);
                        }
                        catch (Exception ex)
                        {
                            log.Info(string.Format("Error getting application {0} details while creating debit order\nError: {1}", id, ex));
                            throw;
                        }
                    }
                    if (applicationDto != null)
                    {
                        var filterConditions = FilterUtil.GetFiltersAsString(BO_Object.Accounts, Convert.ToInt32(HttpContext.Current.Session["UserId"]));
                        filterConditions += " AND AccountId = " + applicationDto.AccountId;

                        try
                        {
                            if (applicationDto != null)
                            {
                                try
                                {
                                    var terminal = new XPQuery<TCC_Branch>(uow).Where(x => x.TerminalId == applicationDto.Disbursement.TCCTerminalID).FirstOrDefault();
                                    CardSwipe.AuthRsp serviceResult = new CardSwipe.AuthRsp();
                                    Atlas.Domain.DTO.Account.ACC_DebitOrderDTO dbtOrder = new Atlas.Domain.DTO.Account.ACC_DebitOrderDTO();
                                    string branchCode = new XPQuery<BRN_Branch>(uow).Where(x => x.BranchId == applicationDto.ApplicationClient.Branch.BranchId).FirstOrDefault().LegacyBranchNum;
                                    string Contractno = CommonHelper.GetValidContractNumber(branchCode, Convert.ToString(applicationDto?.ClientId), Convert.ToString(applicationDto?.AccountId), applicationDto?.Quotation?.VAP == true, false);  //branchCode + "x" + applicationDto.ApplicationClient.ApplicationClientId.ToString() + "x" + applicationDto.ApplicationId.ToString() + (applicationDto.Quotation.VAP ? "xV" : "x"),
                                    if (ConfigurationManager.AppSettings["tccbypass"] == "false")
                                    {
                                        CardDebitOrder cardDebitOrder = new CardDebitOrder()
                                        {
                                            Contract_no = Contractno,  //branchCode + "x" + applicationDto.ApplicationClient.ApplicationClientId.ToString() + "x" + applicationDto.ApplicationId.ToString() + (applicationDto.Quotation.VAP ? "xV" : "x"),
                                            install_amnt = (Convert.ToInt32((applicationDto.Quotation.TotalInstallment) * 100)).ToString(),
                                            contract_amnt = (Convert.ToInt32((applicationDto.Quotation.RepaymentAmount) * 100)).ToString(),
                                            installments = Convert.ToInt32(applicationDto.Quotation.QuantityInstallments).ToString(),
                                            frequency = new Atlas.ThirdParty.NuPay.Util.TCCHelper().GetTCCFrequencyValue(applicationDto.Employer.SalaryType.ToString()),
                                            start_date = (Convert.ToDateTime(applicationDto.Quotation.FirstRepayDate)).ToString("yyyyMMdd"),
                                            employer = applicationDto.Employer.EmployerCode?.ToString(),
                                            adj_rule = ConfigurationManager.AppSettings["adj_rule"],
                                            tracking = ConfigurationManager.AppSettings["tracking"],
                                            client_ID = applicationDto.ApplicationClient.IDNumber, //applicationDto.AccountId.ToString(),
                                            customScreen = true,
                                            panIn = "",
                                            accountNumberIn = (applicationDto.BankDetail == null ? "" : applicationDto.BankDetail.AccountNo),
                                            aedoGlobalTimeout = Convert.ToInt32(ConfigurationManager.AppSettings["aedoGlobalTimeout"]),
                                            Line1 = (applicationDto.ResidentialAddress == null ? "" : applicationDto.ResidentialAddress.AddressLine1),
                                            Line2 = (applicationDto.ResidentialAddress == null ? "" : applicationDto.ResidentialAddress.AddressLine2),
                                            Line3 = (applicationDto.ResidentialAddress == null ? "" : applicationDto.ResidentialAddress.AddressLine3),
                                            Line4 = (applicationDto.ResidentialAddress == null ? "" : applicationDto.ResidentialAddress.AddressLine4 == null ? "" : applicationDto.ResidentialAddress.AddressLine4),
                                            Merchant_ID = terminal.MerchantId,
                                            Term_ID = terminal.TerminalId
                                        };
                                        try
                                        {
                                            log.Info("[fn_CreateDebitOrder]: cardDebitOrder Requeset : " + new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(cardDebitOrder));

                                            serviceResult = new BackOfficeServer.Integrations.Application_Integrations().fn_CreateDebitOrder(cardDebitOrder);

                                            var response = CommonHelper.ErrorHandlingforSwipeCard(serviceResult, applicationDto.AccountId);
                                            if (response != null && response.Status == Constants.Failed.ToString())
                                            {
                                                return response;
                                            }

                                            log.Info("Debit order generated for accountId : " + applicationDto.AccountId.ToString());
                                        }
                                        catch (Exception ex)
                                        {
                                            log.Error(string.Format("Exception occured while debit order generation for accountId : {0} and \nException : {1}", applicationDto.AccountId.ToString(), ex.ToString()));
                                            serviceResult = new CardSwipe.AuthRsp();
                                            throw;
                                        }

                                        dbtOrder = new Atlas.Domain.DTO.Account.ACC_DebitOrderDTO()
                                        {
                                            AccountId = Convert.ToInt64(applicationDto.AccountId),
                                            accountNumber = serviceResult.AccountNumber,
                                            accountType = serviceResult.AccountType,
                                            adjRule = serviceResult.AdjRule,
                                            approvalCode = serviceResult.ApprovalCode,
                                            contractAmount = (serviceResult.ContractAmount == null ? 0 : Convert.ToDecimal(serviceResult.ContractAmount)),
                                            frequency = serviceResult.Frequency,
                                            pAN = serviceResult.PAN,
                                            responseCode = serviceResult.ResponseCode,
                                            tracking = serviceResult.Tracking,
                                            transactionID = string.IsNullOrEmpty(serviceResult.TransactionID) ? "0" : serviceResult.TransactionID,
                                            ContractNumber = cardDebitOrder.Contract_no
                                        };
                                    }
                                    else
                                    {
                                        log.Info("Mock tccbypass is true. Mocking data.\nBy Pass Debit order generated for accountId : " + applicationDto.AccountId.ToString());
                                        branchCode = new XPQuery<BRN_Branch>(uow).Where(x => x.BranchId == applicationDto.ApplicationClient.Branch.BranchId).FirstOrDefault().LegacyBranchNum;
                                        CardDebitOrder cardDebitOrder = new CardDebitOrder()
                                        {
                                            Contract_no = CommonHelper.GetValidContractNumber(branchCode, Convert.ToString(applicationDto?.ClientId), Convert.ToString(applicationDto?.AccountId), applicationDto?.Quotation?.VAP == true, false),//branchCode + applicationDto.ApplicationClient.ToString() + applicationDto.ApplicationId.ToString() + (applicationDto.Quotation.VAP ? "xV" : "x"),
                                            install_amnt = "1",
                                            contract_amnt = "1",
                                            installments = "1",
                                            frequency = "02",
                                            start_date = DateTime.Now.AddDays(10).ToString("yyyyMMdd"),
                                            employer = "01",
                                            adj_rule = "01",
                                            tracking = "14",
                                            client_ID = applicationDto.ApplicationClient.IDNumber.ToString(),
                                            customScreen = true,
                                            panIn = "",
                                            accountNumberIn = (applicationDto.BankDetail == null ? "" : applicationDto.BankDetail.AccountNo),
                                            aedoGlobalTimeout = Convert.ToInt32(ConfigurationManager.AppSettings["aedoGlobalTimeout"]),
                                            Line1 = (applicationDto.ResidentialAddress == null ? "" : applicationDto.ResidentialAddress.AddressLine1),
                                            Line2 = (applicationDto.ResidentialAddress == null ? "" : applicationDto.ResidentialAddress.AddressLine2),
                                            Line3 = (applicationDto.ResidentialAddress == null ? "" : applicationDto.ResidentialAddress.AddressLine3),
                                            Line4 = (applicationDto.ResidentialAddress == null ? "" : applicationDto.ResidentialAddress.AddressLine4 == null ? "" : applicationDto.ResidentialAddress.AddressLine4),
                                            Merchant_ID = terminal.MerchantId,
                                            Term_ID = terminal.TerminalId,
                                        };
                                        try
                                        {
                                            log.Info("[fn_CreateDebitOrder]: cardDebitOrder TestData Request : " + new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(cardDebitOrder));
                                            serviceResult = new Application_Integrations().fn_CreateDebitOrder(cardDebitOrder);

                                            //var response = CommonHelper.ErrorHandlingforSwipeCard(serviceResult, applicationDto.AccountId);
                                            //if (response != null && response.Status == Constants.Failed.ToString())
                                            //{
                                            //    return response;
                                            //}

                                            log.Info("Debit order generated for accountId : " + applicationDto.AccountId.ToString());

                                        }
                                        catch (Exception ex)
                                        {
                                            log.Error(string.Format("Exception occured while debit order generation for accountId : {0} and \nException : {1}", applicationDto.AccountId.ToString(), ex.ToString()));
                                            serviceResult = new CardSwipe.AuthRsp();
                                            throw;
                                        }

                                        serviceResult = new CardSwipe.AuthRsp();
                                        serviceResult.ResponseCode = "00";
                                        dbtOrder = new Atlas.Domain.DTO.Account.ACC_DebitOrderDTO()
                                        {
                                            AccountId = Convert.ToInt64(applicationDto.AccountId),
                                            accountNumber = "0000004064271453",
                                            accountType = "Cheque",
                                            adjRule = "Move Fwd",
                                            approvalCode = "",
                                            contractAmount = applicationDto.Quotation.LoanAmount,
                                            frequency = applicationDto.Employer.SalaryType.ToString(),
                                            pAN = "4451470012438140",
                                            responseCode = "00-Approved or completed successfully",
                                            tracking = "3 days",
                                            transactionID = "0153700636",
                                            ContractNumber = cardDebitOrder.Contract_no
                                        };
                                    }

                                    SaveDebitOrderResponse(dbtOrder);
                                    log.Info("[fn_CreateDebitOrder]: Response : " + new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(serviceResult));
                                    if (serviceResult.ResponseCode.Contains("00"))
                                    {
                                        return Response<CardSwipe.AuthRsp>.CreateResponse(Constants.Success, serviceResult, null);
                                    }
                                    else
                                        return Response<CardSwipe.AuthRsp>.CreateResponse(Constants.Failed, null, new ErrorHandler() { ErrorCode = serviceResult.ResponseCode, Message = EnumCheck.Description((Enum_AEDO_Auth_Req)Convert.ToInt32(serviceResult.ResponseCode)) });
                                }
                                catch (Exception ex)
                                {
                                    log.Error(string.Format("Swipe card error while creating debit order\nError: {0}", ex));
                                    throw;
                                }
                            }
                            else
                            {
                                log.Info("Invalid bank details for creating debit order");
                                return Response<CardSwipe.AuthRsp>.CreateResponse(Constants.Failed, null, new ErrorHandler() { ErrorCode = Constants.ApplicationErrorCode, Message = Constants.InvalidBankDetails });
                            }
                        }
                        catch (Exception ex)
                        {
                            log.Error(string.Format("Error getting account details while creating debit order\nError: {0}", ex));
                            throw;
                        }
                    }
                    else
                    {
                        log.Info("Debit order creation failed.");
                        return Response<CardSwipe.AuthRsp>.CreateResponse(Constants.Failed, null, new ErrorHandler() { ErrorCode = Constants.ApplicationErrorCode, Message = Constants.ApplicationDetailsNotFound });
                    }

                }
            }
            catch (Exception ex)
            {
                log.Info(ex.ToString());
                return Response<CardSwipe.AuthRsp>.CreateResponse(Constants.Failed, null, new ErrorHandler() { ErrorCode = Constants.ApplicationErrorCode, Message = Constants.DebitOrderCreationErrorMessage });
            }
        }



        [NonAction]
        public Response<Integrations.RootObject> fn_IssueNuCard(int id, string voucherNumber)
        {
            try
            {
                log.Info(string.Format("issue NuCard for application {0}", id));
                ApplicationDto application = new ApplicationDto();
                using (var result = new BackOfficeWebServiceClient("BackOfficeWebServer.NET"))
                {
                    try
                    {
                        var filterConditions = FilterUtil.GetFiltersAsString(BO_Object.Applications, Convert.ToInt32(HttpContext.Current.Session["UserId"]));
                        application = result.GetApplicationDetail(filterConditions, id);
                    }
                    catch (Exception ex)
                    {
                        log.Info(string.Format("Error getting application {0} details while issuing NuCard\nError: {1}", id, ex));
                        throw;
                    }
                }
                if (application != null)
                {
                    try
                    {
                        Atlas.ThirdParty.NuPay.Models.NuCardDetails issueNuCard = new Atlas.ThirdParty.NuPay.Models.NuCardDetails()
                        {
                            userId = Convert.ToString(ConfigurationManager.AppSettings["userid"]),
                            userPassword = Convert.ToString(ConfigurationManager.AppSettings["password"]),
                            terminalID = Convert.ToString(ConfigurationManager.AppSettings["terminal"]),
                            profileNumber = Convert.ToString(ConfigurationManager.AppSettings["profile"]),
                            voucherNumber = voucherNumber,
                            cellNumber = application.ApplicationClient.CellNo,
                            clientRef = application.ApplicationClient.ApplicationClientId.ToString(),
                            firstName = application.ApplicationClient.Firstname,
                            lastName = application.ApplicationClient.Surname,
                            idNumber = application.ApplicationClient.IDNumber
                        };
                        var serviceResult = new Atlas.ThirdParty.NuPay.Integrations.NuCardIntegration().IssueNuCard(issueNuCard);
                        var json = JsonConvert.SerializeXmlNode(serviceResult);
                        var results = JsonConvert.DeserializeObject<Integrations.IssueCardResponse>(json);
                        if (results.response.tutukaResponse != null)
                        {
                            results.response.tutukaResponse = results.response.tutukaResponse.Replace("<?xml version=\"1.0\" encoding=\"UTF-8\"?>", "");
                            System.Xml.XmlDocument doc = new System.Xml.XmlDocument(); doc.LoadXml(results.response.tutukaResponse);
                            var jsonObj = JsonConvert.DeserializeObject<Integrations.RootObject>(JsonConvert.SerializeXmlNode(doc));

                            var resultText = jsonObj.methodResponse.@params.param.value.@struct.member.Where(a => a.name == "resultText").Select(a => a.value.@string).FirstOrDefault().ToLower();

                            //check if new client and response is ok
                            if (CheckToAllocateNewCard((int)application.ClientId, application.ApplicationClient.IDNumber) && resultText == "ok")
                            {
                                return Response<Integrations.RootObject>.CreateResponse(Constants.Success, jsonObj, null);
                            }

                            //if existing client then response can be okay or card already allocated
                            else if (resultText == "ok" || resultText == "card already allocated")
                            {
                                var value = new Integrations.Value2
                                {
                                    @string = "ok"
                                };
                                jsonObj.methodResponse.@params.param.value.@struct.member.Where(a => a.name == "resultText").FirstOrDefault().value = value;
                                return Response<Integrations.RootObject>.CreateResponse(Constants.Success, jsonObj, null);
                            }
                            else
                                return Response<Integrations.RootObject>.CreateResponse(Constants.Failed, jsonObj, new ErrorHandler() { ErrorCode = jsonObj.methodResponse.@params.param.value.@struct.member.Where(a => a.name == "resultCode").Select(a => a.value.@int).FirstOrDefault(), Message = jsonObj.methodResponse.@params.param.value.@struct.member.Where(a => a.name == "resultText").Select(a => a.value.@string).FirstOrDefault() });
                        }
                        else
                            return Response<Integrations.RootObject>.CreateResponse(Constants.Failed, null, new ErrorHandler() { ErrorCode = results.response.errorCode, Message = results.response.errorMessage });
                    }
                    catch (Exception ex)
                    {
                        log.Info(string.Format("Service result error while issuing NuCard\nError: {1}", id, ex));
                        throw;
                    }
                }
                else
                {
                    log.Info(string.Format("details not found application {0} details while issuing NuCard", id));
                    return Response<Integrations.RootObject>.CreateResponse(Constants.Failed, null, new ErrorHandler() { ErrorCode = Constants.ApplicationErrorCode, Message = Constants.ApplicationDetailsNotFound });
                }
            }
            catch (Exception ex)
            {
                log.Info(string.Format("Error issuing NuCard for applcation {0}\nError: {1}", id, ex));
                return Response<Integrations.RootObject>.CreateResponse(Constants.Failed, null, new ErrorHandler() { ErrorCode = Constants.ApplicationErrorCode, Message = Constants.NuCardIssueErrorMessage });
            }
        }


        //[HttpGet]
        //[Route("Applications/fn_LoadNuCard/{id}/{voucherNumber}")]
        [NonAction]
        public Response<BackOfficeWebServer.RootObject> fn_LoadNuCard(int id, string voucherNumber)
        {
            try
            {
                log.Info(string.Format("Load NuCard for application {0}", id));
                ApplicationDto application = new ApplicationDto();
                using (var result = new BackOfficeWebServiceClient("BackOfficeWebServer.NET"))
                {
                    try
                    {
                        var filterConditions = FilterUtil.GetFiltersAsString(BO_Object.Applications, Convert.ToInt32(HttpContext.Current.Session["UserId"]));
                        application = result.GetApplicationDetail(filterConditions, id);
                    }
                    catch (Exception ex)
                    {
                        log.Info(string.Format("Error getting application {0} details while issuing NuCard\nError: {1}", id, ex));
                        throw;
                    }
                }
                if (application != null)
                {
                    try
                    {
                        Atlas.ThirdParty.NuPay.Models.NuCardDetails loadNuCard = new Atlas.ThirdParty.NuPay.Models.NuCardDetails()
                        {
                            userId = Convert.ToString(ConfigurationManager.AppSettings["userid"]),
                            userPassword = Convert.ToString(ConfigurationManager.AppSettings["password"]),
                            terminalID = Convert.ToString(ConfigurationManager.AppSettings["terminal"]),
                            profileNumber = Convert.ToString(ConfigurationManager.AppSettings["profile"]),
                            voucherNumber = voucherNumber,
                            requestAmount = Convert.ToInt64(application.Amount * 100).ToString()
                        };
                        log.Info("[fn_LoadNuCard]: fn_LoadNuCard Request : " + new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(loadNuCard));
                        var serviceResult = new Atlas.ThirdParty.NuPay.Integrations.NuCardIntegration().LoadNuCard(loadNuCard);
                        var json = JsonConvert.SerializeXmlNode(serviceResult);
                        var results = JsonConvert.DeserializeObject<IssueCardResponse>(json);

                        if (results.response.tutukaResponse != null)
                        {
                            results.response.tutukaResponse = results.response.tutukaResponse.Replace("<?xml version=\"1.0\" encoding=\"UTF-8\"?>", "");
                            System.Xml.XmlDocument doc = new System.Xml.XmlDocument(); doc.LoadXml(results.response.tutukaResponse);
                            log.Info("[fn_LoadNuCard]: fn_LoadNuCard Response : " + doc);
                            var jsonObj = JsonConvert.DeserializeObject<BackOfficeWebServer.RootObject>(JsonConvert.SerializeXmlNode(doc));
                            if (jsonObj.methodResponse.@params.param.value.@struct.member.Where(a => a.name == "resultText").Select(a => a.value.@string).FirstOrDefault().ToLower() == "ok")
                            {
                                using (var result = new BackOfficeWebServiceClient("BackOfficeWebServer.NET"))
                                {
                                    result.UpdateLoadNuCardResult(application.Disbursement.DisbursementId, jsonObj);
                                }
                                return Response<BackOfficeWebServer.RootObject>.CreateResponse(Constants.Success, jsonObj, null);
                            }
                            else
                            #region NuCard Bypass
                            if (ConfigurationManager.AppSettings["LoadNuCard_ByPass"] == "true")
                            {
                                log.Info("Mock LoadNuCard_ByPass is true. Mocking response.");
                                return Response<BackOfficeWebServer.RootObject>.CreateResponse(Constants.Success, jsonObj, null);

                            }
                            #endregion
                            //return Response<BackOfficeWebServer.RootObject>.CreateResponse(Constants.Failed, jsonObj, new ErrorHandler() { ErrorCode = jsonObj.methodResponse.@params.param.value.@struct.member.Where(a => a.name == "resultCode").Select(a => a.value.@int).FirstOrDefault(), Message = jsonObj.methodResponse.@params.param.value.@struct.member.Where(a => a.name == "resultText").Select(a => a.value.@string).FirstOrDefault() });

                            string errroCode = jsonObj.methodResponse.@params.param.value.@struct.member.Where(a => a.name == "resultCode").Select(a => a.value.@int).FirstOrDefault();
                            log.Info("[fn_LoadNuCard]: fn_LoadNuCard Response : Response Code: " + errroCode + " for applicationid: " + id);

                            string ErrorMessage = CommonHelper.GetErrorMessage(errroCode, BackOfficeEnum.ErrorCategory.LOADNUCARD.ToString());

                            log.Error("[fn_LoadNuCard]: fn_LoadNuCard Response : Response Code: " + errroCode + " Message : " + ErrorMessage + " for applicationid: " + id);
                            return Response<BackOfficeWebServer.RootObject>.CreateResponse(Constants.Failed, null, new ErrorHandler() { ErrorCode = errroCode, Message = ErrorMessage });
                        }
                        else
                        {
                            //log.Info(string.Format("Load NuCard failed for application {0}", id));
                            //return Response<BackOfficeWebServer.RootObject>.CreateResponse(Constants.Failed, null, new ErrorHandler() { ErrorCode = results.response.errorMessage, Message = results.response.errorMessage });
                            log.Info("[fn_LoadNuCard]: fn_LoadNuCard Response : Response Code: " + results.response.errorCode + " for applicationid: " + id);

                            string ErrorMessage = CommonHelper.GetErrorMessage(results.response.errorCode, BackOfficeEnum.ErrorCategory.LOADNUCARD.ToString());

                            log.Error("[fn_LoadNuCard]: fn_LoadNuCard Response : Response Code: " + results.response.errorCode + " Message : " + ErrorMessage + " for applicationid: " + id);
                            return Response<BackOfficeWebServer.RootObject>.CreateResponse(Constants.Failed, null, new ErrorHandler() { ErrorCode = results.response.errorCode, Message = ErrorMessage });
                        }
                    }
                    catch (Exception ex)
                    {
                        log.Info(string.Format("Service result error while loading NuCard\nError: {1}", id, ex));
                        throw;
                    }
                }
                else
                {
                    log.Info(string.Format("details not found application {0} details while loading NuCard", id));
                    return Response<BackOfficeWebServer.RootObject>.CreateResponse(Constants.Failed, null, new ErrorHandler() { ErrorCode = Constants.ApplicationErrorCode, Message = Constants.ApplicationDetailsNotFound });
                }
            }
            catch (Exception ex)
            {
                log.Info(string.Format("Error loading NuCard for applcation {0}\nError: {1}", id, ex));
                return Response<BackOfficeWebServer.RootObject>.CreateResponse(Constants.Failed, null, new ErrorHandler() { ErrorCode = Constants.ApplicationErrorCode, Message = Constants.NuCardLoadErrorMessage });
            }
        }

        [HttpGet]
        [Route("applications/fn_PrintQuote/{id}")]
        public HttpResponseMessage fn_PrintQuote(int id)
        {
            log.Info(string.Format("Get PDF for application {0}", id));

            try
            {
                string[] path = new string[6];

                path[0] = getFilePath("Quotations", "_Quota", id, "Client"); ;
                path[1] = getFilePath("VAP_Contract", "mag_quot_", id, "Client"); ;
                path[2] = getFilePath("CreditLife", "INSURE_", id, "Client");

                path[3] = getFilePath("Quotations", "_Quota", id, "Atlas"); ;
                path[4] = getFilePath("VAP_Contract", "mag_quot_", id, "Atlas"); ;
                path[5] = getFilePath("CreditLife", "INSURE_", id, "Atlas");

                //Merge Multiple Pdf
                MergeMultiplePDFIntoSinglePDF(getFilePath("CustomerAcceptanceContracts", "_Quota", id), path);

                //Reading file as a stream
                var stream = File.OpenRead(getFilePath("CustomerAcceptanceContracts", "_Quota", id));

                // Converting Pdf files to the byte array
                var result = FileToHttpResponseMessage(stream, "file.pdf", "application/pdf", id);

                // Returning file as a Byte Array
                return result;
            }
            catch
            {
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }
        }

        [HttpPost]
        [Route("Applications/{applicationId}/edit/affordability")]
        public Response<string> EditAffordability(AffordabilityDto affordability, int applicationId, int newStatusId)
        {
            try
            {
                using (var uow = new UnitOfWork())
                {
                    log.Info(string.Format("edit affordability for application {0}", applicationId));
                    var role = this.HasAccess(BO_Object.Applications, BackOfficeEnum.Action.EDIT);
                    ErrorHandler Error = new ErrorHandler();
                    if (role.Status == Constants.Success)
                    {
                        var filterConditions = FilterUtil.GetFiltersAsString(BO_Object.Affordability, Convert.ToInt32(HttpContext.Current.Session["UserId"]));

                        if (affordability != null)
                        {
                            if (affordability.NetSalary == affordability.GetNetSalary())
                            {
                                return Response<string>.CreateResponse(Constants.Failed, null, new ErrorHandler() { ErrorCode = Constants.ApplicationErrorCode, Message = Constants.FormulaNotMatch });
                            }
                            using (var result = new BackOfficeWebServiceClient("BackOfficeWebServer.NET"))
                            {
                                try
                                {
                                    string editedFields = result.EditApplicationAffordability(filterConditions, affordability, applicationId, newStatusId, out Error);
                                    if (!string.IsNullOrEmpty(editedFields))
                                    {
                                        return Response<string>.CreateResponse(Constants.Success, editedFields, null);
                                    }
                                    else
                                    {
                                        return Response<string>.CreateResponse(Constants.Failed, editedFields,
                                           Error);
                                    }
                                }
                                catch (Exception ex)
                                {
                                    log.Error(string.Format("error updating affordability details\nError: {0}", ex));
                                    throw;
                                }
                            }
                        }
                        else
                        {
                            log.Info("Affordability details not found");
                            return Response<string>.CreateResponse(Constants.Failed, null, new ErrorHandler() { ErrorCode = Constants.ApplicationErrorCode, Message = Constants.AffordabilityDetailsNotFoundMessage });
                        }
                    }
                    else
                    {
                        log.Info("Authentication failed for edit affordability");
                        return Response<string>.CreateResponse(Constants.Failed, null, new ErrorHandler() { ErrorCode = Constants.AuthorizationFailedCode, Message = role.Error.ToString() });
                    }
                }
            }
            catch (Exception ex)
            {
                log.Error(string.Format("error updating affordability details\nError: {0}", ex));
                return Response<string>.CreateResponse(Constants.Failed, null, new ErrorHandler() { ErrorCode = Constants.ApplicationErrorCode, Message = Constants.UpdateAffordabilityMessage });
            }
        }

        [NonAction]
        public void UpdateApplicationMasterStatus(long id, int newStatus, string action, string data, string editedFields, BO_ObjectAPI category)
        {
            try
            {
                log.Info(string.Format("Update application {0} master status", id));
                using (var uow = new UnitOfWork())
                {
                    using (var result = new BackOfficeWebServiceClient("BackOfficeWebServer.NET"))
                    {
                        bool res = false;
                        int appId = Convert.ToInt32(id);
                        var filterConditions = FilterUtil.GetFiltersAsString(BO_Object.Applications, Convert.ToInt32(HttpContext.Current.Session["UserId"]));
                        res = result.UpdateApplicationMasterStatus(filterConditions, appId, newStatus);
                        if (res)
                        {
                            UpdateApplicationEventHistory(id, action, data, editedFields, category);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                log.Error(string.Format("Error updating application {0} master status\nError: {1}", id, ex));
                throw;
            }
        }

        [NonAction]
        public void UpdateApplicationClientStatus(long id, int newStatus, string action, string data, string editedFields, BO_ObjectAPI category)
        {
            log.Info(string.Format("Update application {0} client status", id));
            try
            {
                try
                {
                    log.Info("get edited fields");
                    if (!string.IsNullOrEmpty(editedFields) && editedFields != "[]")
                    {
                        if (!editedFields.Contains("Status"))
                        {
                            editedFields = getEditedFields(editedFields, (int)id, newStatus, BO_ObjectAPI.client);
                        }
                    }
                    else
                    {
                        var lstEditedFields = new List<VMEditedFields>();
                        lstEditedFields.Add(new VMEditedFields() { FieldName = "Status", OldValue = null, NewValue = "EDITED" });
                        editedFields = JsonConvert.SerializeObject(lstEditedFields);
                    }
                }
                catch (Exception ex)
                {
                    log.Error(string.Format("Error in get edited fields \nError: {1}", id, ex));
                    throw;
                }
                using (var uow = new UnitOfWork())
                {
                    using (var result = new BackOfficeWebServiceClient("BackOfficeWebServer.NET"))
                    {
                        bool res = false;
                        int appId = Convert.ToInt32(id);
                        var filterConditions = FilterUtil.GetFiltersAsString(BO_Object.Applications, Convert.ToInt32(HttpContext.Current.Session["UserId"]));
                        res = result.UpdateApplicationClientStatus(filterConditions, appId, newStatus);
                        if (res)
                        {
                            UpdateApplicationEventHistory(id, action, data, editedFields, category);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                log.Error(string.Format("Error updating application {0} client status\nError: {1}", id, ex));
                throw;
            }
        }

        [NonAction]
        public void UpdateApplicationBankStatus(long id, int newStatus, string action, string data, string editedFields, BO_ObjectAPI category)
        {
            try
            {
                log.Info(string.Format("Update application {0} bank status", id));

                try
                {
                    log.Info("get edited fields");
                    if (!string.IsNullOrEmpty(editedFields) && editedFields != "[]")
                    {
                        if (!editedFields.Contains("Status"))
                        {
                            editedFields = getEditedFields(editedFields, (int)id, newStatus, BO_ObjectAPI.bankdetails);
                        }
                    }
                    else
                    {
                        var lstEditedFields = new List<VMEditedFields>
                        {
                            new VMEditedFields() { FieldName = "Status", OldValue = null, NewValue = "EDITED" }
                        };
                        editedFields = JsonConvert.SerializeObject(lstEditedFields);
                    }
                }
                catch (Exception ex)
                {
                    log.Error(string.Format("Error in get edited fields \nError: {1}", id, ex));
                    throw;
                }

                using (var uow = new UnitOfWork())
                {
                    using (var result = new BackOfficeWebServiceClient("BackOfficeWebServer.NET"))
                    {
                        bool res = false;
                        int appId = Convert.ToInt32(id);
                        var filterConditions = FilterUtil.GetFiltersAsString(BO_Object.Applications, Convert.ToInt32(HttpContext.Current.Session["UserId"]));
                        res = result.UpdateApplicationBankStatus(filterConditions, (int)id, newStatus);
                        if (res)
                        {
                            UpdateApplicationEventHistory(id, action, data, editedFields, category);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                log.Error(string.Format("Error updating application {0} bank status\nError: {1}", id, ex));
                throw;
            }
        }

        [HttpPost]
        [Route("Applications/{applicationId}/edit/disbursement")]
        public Response<string> EditDisbursement(DisbursementDto disbursement, int applicationId, int newStatusId)
        {

            try
            {
                log.Info(string.Format("Edit disbursment for application {0}", applicationId));
                using (var uow = new UnitOfWork())
                {
                    var role = this.HasAccess(BO_Object.Applications, BackOfficeEnum.Action.VIEW);
                    if (role.Status == Constants.Success)
                    {
                        if (disbursement != null)
                        {
                            //if (!string.IsNullOrEmpty(disbursement.NuCardNumber))
                            //{
                            using (var result = new BackOfficeWebServiceClient("BackOfficeWebServer.NET"))
                            {
                                var applicationDto = GetApplicationById(applicationId)?.Data.ApplicationDetail;  //result.GetApplicationDetail("1=1", applicationId);
                                bool NuCardStatus;
                                if (applicationDto.CardInfo?.AllocateNuCard == true)
                                {
                                    NuCardStatus = CommonHelper.ValidateVoucherNumber(disbursement.NuCardNumber, true);
                                }
                                else if (!string.IsNullOrEmpty(disbursement.CardDetails?.NewVocherNumber) && disbursement.CardDetails?.OldVocherNumber != disbursement.CardDetails?.NewVocherNumber)
                                {
                                    NuCardStatus = CommonHelper.ValidateVoucherNumber(disbursement.CardDetails.NewVocherNumber, true);
                                }
                                else
                                {
                                    NuCardStatus = CommonHelper.ValidateVoucherNumber(disbursement.NuCardNumber, false);
                                }

                                if (NuCardStatus)
                                {
                                    return SaveDisbursementDetails(disbursement, applicationId, newStatusId);
                                }
                                else
                                {
                                    log.Info(string.Format("Update disbursement failed for application {0}. Error: {1}", applicationId, NuCardStatus));
                                    return Response<string>.CreateResponse(Constants.Failed, null, new ErrorHandler() { ErrorCode = Constants.ApplicationErrorCode, Message = Constants.NuCardNotValid });
                                }
                            }
                            //}
                            //else
                            //{
                            //  log.Info(string.Format("Update disbursement failed for application {0}. NuCard number not found", applicationId));
                            //return Response<string>.CreateResponse(Constants.Failed, null, new ErrorHandler() { ErrorCode = Constants.ApplicationErrorCode, Message = "Please enter NuCard number" });
                            //}
                        }
                        else
                        {
                            log.Info("Update disbursement failed. Request payload is null");
                            return Response<string>.CreateResponse(Constants.Failed, null, new ErrorHandler() { ErrorCode = Constants.ApplicationErrorCode, Message = Constants.UpdateDisbursementMessage });
                        }
                    }
                    else
                    {
                        log.Info(string.Format("Authentication Failed. {0}", role.Error));
                        return Response<string>.CreateResponse(Constants.Failed, null, new ErrorHandler() { ErrorCode = Constants.AuthorizationFailedCode, Message = role.Error.ToString() });
                    }
                }
            }
            catch (Exception ex)
            {
                log.Error(string.Format("Error updating disbursement details for application {0}\nError: {1}", applicationId, ex));
                return Response<string>.CreateResponse(Constants.Failed, null, new ErrorHandler() { ErrorCode = Constants.ApplicationErrorCode, Message = Constants.UpdateDisbursementMessage });
            }
        }

        [HttpPost]
        [Route("Applications/{applicationId}/savedisbursementdetails")]
        public Response<string> SaveDisbursementDetails(DisbursementDto disbursement, int applicationId, int newStatusId)
        {

            try
            {
                log.Info(string.Format("Edit disbursment for application {0}", applicationId));
                using (var uow = new UnitOfWork())
                {
                    var role = this.HasAccess(BO_Object.Applications, BackOfficeEnum.Action.VIEW);
                    if (role.Status == Constants.Success)
                    {
                        if (disbursement != null)
                        {
                            //if (!string.IsNullOrEmpty(disbursement.NuCardNumber))
                            {
                                {
                                    using (var result = new BackOfficeWebServiceClient("BackOfficeWebServer.NET"))
                                    {
                                        var filterConditions = FilterUtil.GetFiltersAsString(BO_Object.Disbursement, Convert.ToInt32(HttpContext.Current.Session["UserId"]));

                                        string editedFields = result.EditApplicationDisbursement(filterConditions, disbursement, applicationId, newStatusId);
                                        UpdateNuCardClientMapping(applicationId);

                                        if (!string.IsNullOrEmpty(editedFields))
                                        {
                                            log.Info(string.Format("Disbursement update success for application {0}", applicationId));
                                            return Response<string>.CreateResponse(Constants.Success, editedFields, null);
                                        }
                                        else
                                        {
                                            log.Info(string.Format("Update disbursement failed. Details not found. ApplicationId {0}", applicationId));
                                            return Response<string>.CreateResponse(Constants.Failed, null, new ErrorHandler() { ErrorCode = Constants.ApplicationErrorCode, Message = Constants.DisbursementNotFoundErrorMessage });
                                        }
                                    }
                                }
                            }
                            //else
                            //{
                            //  log.Info(string.Format("Update disbursement failed for application {0}. NuCard number not found", applicationId));
                            // return Response<string>.CreateResponse(Constants.Failed, null, new ErrorHandler() { ErrorCode = Constants.ApplicationErrorCode, Message = "Please enter NuCard number" });
                            //}
                        }
                        else
                        {
                            log.Info("Update disbursement failed. Request payload is null");
                            return Response<string>.CreateResponse(Constants.Failed, null, new ErrorHandler() { ErrorCode = Constants.ApplicationErrorCode, Message = Constants.UpdateDisbursementMessage });
                        }
                    }
                    else
                    {
                        log.Info(string.Format("Authentication Failed. {0}", role.Error));
                        return Response<string>.CreateResponse(Constants.Failed, null, new ErrorHandler() { ErrorCode = Constants.AuthorizationFailedCode, Message = role.Error.ToString() });
                    }
                }
            }
            catch (Exception ex)
            {
                log.Error(string.Format("Error updating disbursement details for application {0}\nError: {1}", applicationId, ex));
                return Response<string>.CreateResponse(Constants.Failed, null, new ErrorHandler() { ErrorCode = Constants.ApplicationErrorCode, Message = Constants.UpdateDisbursementMessage });
            }
        }

        public void UpdateApplicationAffordabilityStatus(long id, int newStatus, string action, string data, string editedFields, BO_ObjectAPI category)
        {
            try
            {
                try
                {
                    log.Info("get edited fields");
                    if (!string.IsNullOrEmpty(editedFields) && editedFields != "[]")
                        editedFields = getEditedFields(editedFields, (int)id, newStatus, BO_ObjectAPI.affordability);
                    else
                    {
                        var lstEditedFields = new List<VMEditedFields>
                        {
                            new VMEditedFields() { FieldName = "Status", OldValue = null, NewValue = "EDITED" }
                        };
                        editedFields = JsonConvert.SerializeObject(lstEditedFields);
                    }
                }
                catch (Exception ex)
                {
                    log.Error(string.Format("Error in get edited fields \nError: {1}", id, ex));
                    throw;
                }

                log.Info(string.Format("Update application {0} affordability status", id));
                using (var uow = new UnitOfWork())
                {
                    using (var result = new BackOfficeWebServiceClient("BackOfficeWebServer.NET"))
                    {
                        bool res = false;
                        int clientid = Convert.ToInt32(id);
                        var filterConditions = FilterUtil.GetFiltersAsString(BO_Object.Affordability, Convert.ToInt32(HttpContext.Current.Session["UserId"]));
                        res = result.UpdateApplicationAffordabilityStatus(filterConditions, clientid, newStatus);
                        if (res)
                        {
                            UpdateApplicationEventHistory(id, action, data, editedFields, category);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                log.Error(string.Format("Error updating application {0} affordability status\nError: {1}", id, ex));
                throw;
            }
        }

        public void UpdateApplicationDisbursementStatus(long id, int newStatus, string action, string data, string editedFields, BO_ObjectAPI category)
        {
            try
            {
                log.Info(string.Format("Update application {0} disbursement status", id));

                try
                {
                    log.Info("get edited fields");
                    if (!string.IsNullOrEmpty(editedFields) && editedFields != "[]")
                        editedFields = getEditedFields(editedFields, (int)id, newStatus, BO_ObjectAPI.disbursement);
                    else
                    {
                        var lstEditedFields = new List<VMEditedFields>
                        {
                            new VMEditedFields() { FieldName = "Status", OldValue = null, NewValue = "EDITED" }
                        };
                        editedFields = JsonConvert.SerializeObject(lstEditedFields);
                    }
                }
                catch (Exception ex)
                {
                    log.Error(string.Format("Error in get edited fields \nError: {1}", id, ex));
                    throw;
                }

                using (var uow = new UnitOfWork())
                {
                    using (var result = new BackOfficeWebServiceClient("BackOfficeWebServer.NET"))
                    {
                        bool res = false;
                        int clientid = Convert.ToInt32(id);
                        var filterConditions = FilterUtil.GetFiltersAsString(BO_Object.Disbursement, Convert.ToInt32(HttpContext.Current.Session["UserId"]));
                        res = result.UpdateApplicationDisbursementStatus(filterConditions, clientid, newStatus);
                        if (res)
                        {
                            UpdateApplicationEventHistory(id, action, data, editedFields, category);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                log.Error(string.Format("Error updating application {0} disbursement status\nError: {1}", id, ex));
                throw;
            }
        }

        public void UpdateApplicationQuotationStatus(long id, int newStatus, string action, string data, string editedFields, BO_ObjectAPI category)
        {
            try
            {
                log.Info(string.Format("Update application {0} quotation status", id));
                try
                {
                    log.Info("get edited fields");
                    if (!string.IsNullOrEmpty(editedFields) && editedFields != "[]")
                        editedFields = getEditedFields(editedFields, (int)id, newStatus, BO_ObjectAPI.quotation);
                    else
                    {
                        var lstEditedFields = new List<VMEditedFields>
                        {
                            new VMEditedFields() { FieldName = "Status", OldValue = null, NewValue = "EDITED" }
                        };
                        editedFields = JsonConvert.SerializeObject(lstEditedFields);
                    }
                }
                catch (Exception ex)
                {
                    log.Error(string.Format("Error in get edited fields \nError: {1}", id, ex));
                    throw;
                }
                using (var uow = new UnitOfWork())
                {
                    using (var result = new BackOfficeWebServiceClient("BackOfficeWebServer.NET"))
                    {
                        bool res = false;
                        int clientid = Convert.ToInt32(id);
                        var filterConditions = FilterUtil.GetFiltersAsString(BO_Object.Quotation, Convert.ToInt32(HttpContext.Current.Session["UserId"]));
                        res = result.UpdateApplicationQuotationStatus(filterConditions, clientid, newStatus);
                        if (res)
                        {
                            UpdateApplicationEventHistory(id, action, data, editedFields, category);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                log.Error(string.Format("Error updating application {0} quotation status\nError: {1}", id, ex));
                throw;
            }
        }

        public void UpdateApplicationDocumentStatus(long id, int newStatus, string action, string data, string editedFields, BO_ObjectAPI category)
        {
            try
            {
                log.Info(string.Format("Update application {0} document status", id));
                try
                {
                    log.Info("get edited fields");
                    if (!string.IsNullOrEmpty(editedFields) && editedFields != "[]")
                        editedFields = getEditedFields(editedFields, (int)id, newStatus, BO_ObjectAPI.quotation);
                    else
                    {
                        var lstEditedFields = new List<VMEditedFields>
                        {
                            new VMEditedFields() { FieldName = "Status", OldValue = null, NewValue = "EDITED" }
                        };
                        editedFields = JsonConvert.SerializeObject(lstEditedFields);
                    }
                }
                catch (Exception ex)
                {
                    log.Error(string.Format("Error in get edited fields \nError: {1}", id, ex));
                    throw;
                }

                using (var uow = new UnitOfWork())
                {
                    using (var result = new BackOfficeWebServiceClient("BackOfficeWebServer.NET"))
                    {
                        bool res = false;
                        int applicationId = Convert.ToInt32(id);
                        var filterConditions = FilterUtil.GetFiltersAsString(BO_Object.Quotation, Convert.ToInt32(HttpContext.Current.Session["UserId"]));
                        res = result.UpdateApplicationDocumentStatus(filterConditions, applicationId, newStatus);
                        if (res)
                        {
                            //  TODO : Fix history update - Edited fild is null
                            // UpdateApplicationEventHistory(id, action, data, editedFields, category);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                log.Error(string.Format("Error updating application {0} quotation status\nError: {1}", id, ex));
                throw;
            }
        }

        public void UpdateApplicationEventHistory(long id, string action, string data, string editedFields, BO_ObjectAPI category)
        {
            try
            {
                log.Info(string.Format("update event history for application {0}, category {1}, action {2}", id, category, action));
                using (var uow = new UnitOfWork())
                {

                    ViewModels.VMApplications json = JsonConvert.DeserializeObject<ViewModels.VMApplications>(data);
                    var history = new BOS_ApplicationEventHistory(uow)
                    {
                        User = new XPQuery<Auth_User>(uow).Where(u => u.UserId == Convert.ToInt32(HttpContext.Current.Session["UserId"])).FirstOrDefault(),
                        Role = new XPQuery<BOS_Role>(uow).Where(u => u.RoleId == Convert.ToInt32(HttpContext.Current.Session["RoleId"])).FirstOrDefault(),
                        Action = new XPQuery<BOS_Action>(uow).Where(u => u.ActionName.ToLower() == action.ToLower()).FirstOrDefault(),
                        ActionDate = DateTime.UtcNow,
                        Comment = json.Comment,
                        Category = category.ToString(),
                        FieldsModified = editedFields,
                        ApplicationId = id,
                        Version = (new XPQuery<BOS_ApplicationEventHistory>(uow).Where(hi => hi.Category == category.ToString() && hi.ApplicationId == id).Count() + 1)
                    };
                    uow.CommitChanges();
                }
            }
            catch (Exception ex)
            {
                log.Error(string.Format("Error updating event history for application {0}, category {1}, action {2}\nError: {3}", id, category, action, ex));
                throw;
            }
        }

        [HttpPost]
        [Route("Applications/{applicationId}/edit/quotation")]
        public Response<long> EditQuotation(QuotationDto quotationDto, int applicationId, int newStatusId)
        {
            try
            {
                log.Info(string.Format("Edit quotation for application {0}", applicationId));
                var role = this.HasAccess(BO_Object.Applications, BackOfficeEnum.Action.EDIT);
                if (role.Status == Constants.Success)
                {
                    var filterConditions = FilterUtil.GetFiltersAsString(BO_Object.Quotation, Convert.ToInt32(HttpContext.Current.Session["UserId"]));
                    quotationDto.CreateUserId = Convert.ToInt32(HttpContext.Current.Session["UserId"]);
                    quotationDto.Application = new ApplicationDto();
                    quotationDto.Application.ApplicationId = applicationId;
                    if (quotationDto != null)
                    {
                        using (var result = new BackOfficeWebServiceClient("BackOfficeWebServer.NET"))
                        {
                            log.Info(string.Format("Update quoation for application {0} success", applicationId));
                            quotationDto.QuotationId = result.AddUpdateQuotation(filterConditions, quotationDto, newStatusId);
                            return Response<long>.CreateResponse(Constants.Success, quotationDto.QuotationId, null);
                        }
                    }
                    else
                    {
                        log.Info(string.Format("Update quoation for application {0} failed. Request payload is null.", applicationId));
                        return Response<long>.CreateResponse(Constants.Failed, 0, new ErrorHandler() { ErrorCode = Constants.ApplicationErrorCode, Message = Constants.UpdateQuotationMessage });
                    }
                }
                else
                {
                    log.Info(string.Format("Authentication Failed. {0}", role.Error));
                    return Response<long>.CreateResponse(Constants.Failed, 0, new ErrorHandler() { ErrorCode = Constants.AuthorizationFailedCode, Message = role.Error.ToString() });
                }
            }
            catch (Exception ex)
            {
                log.Error(string.Format("Error updating qu0tation details for application {0}\nError: {1}", applicationId, ex));
                return Response<long>.CreateResponse(Constants.Failed, 0, new ErrorHandler() { ErrorCode = Constants.ApplicationErrorCode, Message = Constants.UpdateQuotationMessage });

            }
        }

        [NonAction]
        public long Disbursement(long applicationId)
        {
            try
            {
                log.Info(string.Format("Disbursement for application {0}", applicationId));
                List<VMLoanAccount> _loanAccount = new List<VMLoanAccount>();
                ApplicationDto application = null;
                long _accountid = 0;
                using (var result = new BackOfficeWebServiceClient("BackOfficeWebServer.NET"))
                {
                    List<Application_AccountDto> loantype = null;
                    try
                    {
                        var filterConditions = FilterUtil.GetFiltersAsString(BO_Object.Applications, Convert.ToInt32(HttpContext.Current.Session["UserId"]));

                        application = result.GetApplicationDetail(filterConditions, (int)applicationId);

                        if (application != null)
                        {
                            _accountid = Convert.ToInt64(application.AccountId);

                            loantype = application.LoanAccount;
                            if (loantype != null)
                            {
                                for (int i = 0; i < loantype.Count(); i++)
                                {
                                    _loanAccount.Add(new VMLoanAccount { LoanType = loantype[i].AccountType.Description, AccountId = Convert.ToInt64(loantype[i].AccountId) });
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        log.Info(string.Format("Error getting application {0} details for disbursement\nError: {1}", applicationId, ex));
                        throw;
                    }

                    try
                    {
                        var list = result.GetQuotationDetails(applicationId);
                        InsertShedulePayment(Convert.ToInt32(applicationId), list, _loanAccount, list.PeriodType);

                        try
                        {
                            //if nucard allocated, create nucard schedule
                            if ((application.BranchId != 1L) && (CheckToAllocateNewCard((int)application.ClientId, application.ApplicationClient.IDNumber) || !string.IsNullOrEmpty(application.Disbursement.CardDetails?.NewVocherNumber) && application.Disbursement.CardDetails?.OldVocherNumber != application.Disbursement.CardDetails?.NewVocherNumber))
                            {
                                //save NuCard installment
                                using (UnitOfWork uow = new UnitOfWork())
                                {
                                    log.Info($"save nucard installment for application {applicationId}");

                                    //get nucard account
                                    var nuCardAccountId = loantype.FirstOrDefault(x => x.AccountType.Description.ToUpper() == "NUCARD")?.AccountId;
                                    if (nuCardAccountId != null && nuCardAccountId > 0)
                                    {
                                        var nuCardAccount = new XPQuery<ACC_Account>(uow).FirstOrDefault(a => a.AccountId == nuCardAccountId);

                                        if (nuCardAccount != null)
                                        {
                                            log.Info($"NuCard account found. Account id {nuCardAccount.AccountId}");

                                            //get loan account
                                            var loanAccountId = loantype.FirstOrDefault(x => x.AccountType.Description.ToUpper() == "LOAN")?.AccountId;
                                            if (loanAccountId != null && loanAccountId > 0)
                                            {
                                                var loanAccount = new XPQuery<ACC_Account>(uow).FirstOrDefault(a => a.AccountId == loanAccountId);

                                                if (loanAccount != null)
                                                {
                                                    log.Info($"loan account found. Account id {loanAccount.AccountId}. Insert NuCard Schdeule.");

                                                    //insert schedule
                                                    DBManager.SaveNuCardInstallment(nuCardAccount.AccountId, 1, nuCardAccount.LoanAmount, nuCardAccount.LoanAmount, 0, (DateTime)application.Quotation.FirstRepayDate, loanAccount.Period);

                                                    //update nucard account period
                                                    nuCardAccount.Period = loanAccount.Period;
                                                    nuCardAccount.AccountBalance = nuCardAccount.LoanAmount;
                                                    nuCardAccount.InstalmentAmount = nuCardAccount.LoanAmount;
                                                    nuCardAccount.NumOfInstalments = 1;
                                                    nuCardAccount.Status = new XPQuery<ACC_Status>(uow).Where(s => s.StatusId == (int)BackOfficeEnum.NewStatus.OPEN).FirstOrDefault();
                                                    nuCardAccount.NewStatusId = new XPQuery<ACC_Status>(uow).Where(s => s.StatusId == (int)BackOfficeEnum.NewStatus.OPEN).FirstOrDefault().StatusId;
                                                    nuCardAccount.FirstInstalmentDate = (DateTime)application.Quotation.FirstRepayDate;
                                                    //nuCardAccount.OpenDate = DateTime.Now;
                                                    //nuCardAccount.Person.PersonId = Convert.ToUInt32(HttpContext.Current.Session["PersonId"]);
                                                    nuCardAccount.Save();
                                                    uow.CommitChanges();
                                                }
                                            }
                                            else
                                            {
                                                log.Fatal($"Loan account not found for application {applicationId}");
                                                throw new Exception($"Loan account not found for application {applicationId}");
                                            }
                                        }
                                    }
                                    else
                                    {
                                        log.Fatal($"NuCard account not found for application {applicationId}");
                                        throw new Exception($"NuCard account not found for application {applicationId}");
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            log.Error($"error inserting schedule for NuCard.\n{ex}");
                            throw;
                        }
                    }
                    catch (Exception ex)
                    {
                        log.Error(string.Format("Error inserting payment schedule for application {0} in disbursement\nError: {1}", applicationId, ex));
                        throw;
                    }
                }
                using (var Acc = new BackOfficeAccountServerClient("BackOfficeAccountServer.NET"))
                {
                    try
                    {
                        var isSuccess = Acc.OpenAccount(_loanAccount.Select(x => x.AccountId).ToArray());
                        if (isSuccess)
                        {
                            using (var result = new BackOfficeWebServiceClient("BackOfficeWebServer.NET"))
                            {
                                var filterConditions = FilterUtil.GetFiltersAsString(BO_Object.EmployerDetails, Convert.ToInt32(HttpContext.Current.Session["UserId"]));

                                var lstEditedFields = new List<VMEditedFields>
                                {
                                    new VMEditedFields() { FieldName = "Status", OldValue = null, NewValue = "VERIFICATION" }
                                };
                                var editedFields = JsonConvert.SerializeObject(lstEditedFields);

                                ViewModels.VMApplications vmapp = new ViewModels.VMApplications();
                                vmapp.Comment = "Verified";

                                var data = JsonConvert.SerializeObject(vmapp);
                                UpdateApplicationClientStatus(applicationId, 3, "APPLICATION_LOAD_NUCARD", data, editedFields, BO_ObjectAPI.client);
                                UpdateApplicationBankStatus(applicationId, 3, "APPLICATION_LOAD_NUCARD", data, editedFields, BO_ObjectAPI.bankdetails);
                                UpdateApplicationEmployerDetails(applicationId, 3, "APPLICATION_LOAD_NUCARD", data, editedFields, BO_ObjectAPI.employerdetails);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        log.Info(string.Format("Error opening account for application {0}, account id {1} in disbursement\nError: {2}", applicationId, _accountid, ex));
                        throw;
                    }
                };

                return _accountid;
            }
            catch (Exception ex)
            {
                log.Error(string.Format("Error in disbursement for application {0}\nError: {1}", applicationId, ex));
                return 0;
            }
        }

        [NonAction]
        public Response<Integrations.RootObject> fn_DeductNuCard(int id, string voucherNumber)
        {
            try
            {
                log.Info(string.Format("DeductNuCard for application {0}", id));
                ApplicationDto application = new ApplicationDto();
                using (var result = new BackOfficeWebServiceClient("BackOfficeWebServer.NET"))
                {
                    try
                    {
                        var filterConditions = FilterUtil.GetFiltersAsString(BO_Object.Applications, Convert.ToInt32(HttpContext.Current.Session["UserId"]));
                        application = result.GetApplicationDetail(filterConditions, id);
                    }
                    catch (Exception ex)
                    {
                        log.Info(string.Format("Error getting application {0} details while executing DeductNuCard\nError: {1}", id, ex));
                        throw;
                    }
                }
                if (application != null)
                {
                    try
                    {
                        log.Info(string.Format("load nu card for application {0}", id));
                        NuCardDetails loadNuCard = new NuCardDetails()
                        {
                            userId = Convert.ToString(ConfigurationManager.AppSettings["userid"]),
                            userPassword = Convert.ToString(ConfigurationManager.AppSettings["password"]),
                            terminalID = Convert.ToString(ConfigurationManager.AppSettings["terminal"]),
                            profileNumber = Convert.ToString(ConfigurationManager.AppSettings["profile"]),
                            voucherNumber = voucherNumber,
                            requestAmount = Convert.ToInt64(application.Quotation.InstallmentAmount * 100).ToString()
                        };
                        var serviceResult = new Application_Integrations().fn_DeductNuCard(loadNuCard);
                        var json = JsonConvert.SerializeXmlNode(serviceResult);
                        var results = JsonConvert.DeserializeObject<IssueCardResponse>(json);
                        if (results.response.tutukaResponse != null)
                        {
                            try
                            {
                                log.Info(string.Format("Parse tutukaResponse for application {0}", id));
                                results.response.tutukaResponse = results.response.tutukaResponse.Replace("<?xml version=\"1.0\" encoding=\"UTF-8\"?>", "");
                                System.Xml.XmlDocument doc = new System.Xml.XmlDocument(); doc.LoadXml(results.response.tutukaResponse);
                                var jsonObj = JsonConvert.DeserializeObject<Integrations.RootObject>(JsonConvert.SerializeXmlNode(doc));
                                if (jsonObj.methodResponse.@params.param.value.@struct.member.Where(a => a.name == "resultText").Select(a => a.value.@string).FirstOrDefault() == "ok")
                                    return Response<Integrations.RootObject>.CreateResponse(Constants.Success, jsonObj, null);
                                else
                                    return Response<Integrations.RootObject>.CreateResponse(Constants.Failed, jsonObj, new ErrorHandler() { ErrorCode = jsonObj.methodResponse.@params.param.value.@struct.member.Where(a => a.name == "resultCode").Select(a => a.value.@int).FirstOrDefault(), Message = jsonObj.methodResponse.@params.param.value.@struct.member.Where(a => a.name == "resultText").Select(a => a.value.@string).FirstOrDefault() });
                            }
                            catch (Exception ex)
                            {
                                log.Error(string.Format("[fn_DeductNuCard] Error parsing tutukaResponse for application {0}\nError: {1}", id, ex));
                                throw;
                            }
                        }
                        else
                        {
                            log.Info(string.Format("service error while executing DeductNuCard for application {0}. Error {1}", id, results.response.errorMessage));
                            return Response<Integrations.RootObject>.CreateResponse(Constants.Failed, null, new ErrorHandler() { ErrorCode = results.response.errorCode, Message = results.response.errorMessage });
                        }
                    }
                    catch (Exception ex)
                    {
                        log.Error(string.Format("Error executing DeductNuCard for application {0}\nError: {1}", id, ex));
                        throw;
                    }
                }
                else
                {
                    log.Info(string.Format("Application {0} not found while executing DeductNuCard", id));
                    return Response<Integrations.RootObject>.CreateResponse(Constants.Failed, null, new ErrorHandler() { ErrorCode = Constants.ApplicationErrorCode, Message = Constants.ApplicationDetailsNotFound });
                }
            }
            catch (Exception ex)
            {
                log.Error(string.Format("Error executing DeductNuCard for application {0}\nError: {1}", id, ex));
                return Response<Integrations.RootObject>.CreateResponse(Constants.Failed, null, new ErrorHandler() { ErrorCode = Constants.ApplicationErrorCode, Message = Constants.DeductNuCardMessage });
            }
        }

        [HttpGet]
        [Route("Applications/fn_BalanceNuCard/{id}/{voucherNumber}")]
        public Response<Integrations.RootObject> fn_BalanceNuCard(int id, string voucherNumber)
        {
            try
            {
                log.Info(string.Format("BalanceNuCard for application {0}", id));
                ApplicationDto application = new ApplicationDto();
                using (var result = new BackOfficeWebServiceClient("BackOfficeWebServer.NET"))
                {
                    try
                    {
                        var filterConditions = FilterUtil.GetFiltersAsString(BO_Object.Applications, Convert.ToInt32(HttpContext.Current.Session["UserId"]));
                        application = result.GetApplicationDetail(filterConditions, id);
                    }
                    catch (Exception ex)
                    {
                        log.Info(string.Format("Error getting application {0} details while executing BalanceNuCard\nError: {1}", id, ex));
                        throw;
                    }
                }
                if (application != null)
                {
                    try
                    {
                        log.Info(string.Format("load nu card for application {0}", id));
                        Atlas.ThirdParty.NuPay.Models.NuCardDetails loadNuCard = new Atlas.ThirdParty.NuPay.Models.NuCardDetails
                        {
                            userId = Convert.ToString(ConfigurationManager.AppSettings["userid"]),
                            userPassword = Convert.ToString(ConfigurationManager.AppSettings["password"]),
                            terminalID = Convert.ToString(ConfigurationManager.AppSettings["terminal"]),
                            profileNumber = Convert.ToString(ConfigurationManager.AppSettings["profile"]),
                            voucherNumber = voucherNumber
                        };
                        var serviceResult = new Atlas.ThirdParty.NuPay.Integrations.NuCardIntegration().BalanceNuCard(loadNuCard);
                        var json = JsonConvert.SerializeXmlNode(serviceResult);
                        var results = JsonConvert.DeserializeObject<IssueCardResponse>(json);
                        if (results.response.tutukaResponse != null)
                        {
                            try
                            {
                                results.response.tutukaResponse = results.response.tutukaResponse.Replace("<?xml version=\"1.0\" encoding=\"UTF-8\"?>", "");
                                System.Xml.XmlDocument doc = new System.Xml.XmlDocument(); doc.LoadXml(results.response.tutukaResponse);
                                var jsonObj = JsonConvert.DeserializeObject<Integrations.RootObject>(JsonConvert.SerializeXmlNode(doc));
                                return Response<Integrations.RootObject>.CreateResponse(Constants.Success, jsonObj, null);
                            }
                            catch (Exception ex)
                            {
                                log.Error(string.Format("[fn_BalanceNuCard] Error parsing tutukaResponse for application {0}\nError: {1}", id, ex));
                                throw;
                            }
                        }
                        else
                        {
                            log.Info(string.Format("service error while executing BalanceNuCard for application {0}. Error {1}", id, results.response.errorMessage));
                            return Response<Integrations.RootObject>.CreateResponse(Constants.Failed, null, new ErrorHandler() { ErrorCode = results.response.errorCode, Message = results.response.errorMessage });
                        }
                    }
                    catch (Exception ex)
                    {
                        log.Info(string.Format("Error executing BalanceNuCard fpr application {0}\nError: {1}", id, ex));
                        throw;
                    }
                }
                else
                {
                    log.Info(string.Format("Application {0} not found while executing BalanceNuCard", id));
                    return Response<Integrations.RootObject>.CreateResponse(Constants.Failed, null, new ErrorHandler() { ErrorCode = Constants.ApplicationErrorCode, Message = Constants.ApplicationDetailsNotFound });
                }

            }
            catch (Exception ex)
            {
                log.Error(string.Format("Error executing BalanceNuCard for application {0}\nError: {1}", id, ex));
                return Response<Integrations.RootObject>.CreateResponse(Constants.Failed, null, new ErrorHandler() { ErrorCode = Constants.ApplicationErrorCode, Message = Constants.BalanceNuCardMessage });
            }
        }

        [HttpGet]
        [Route("Applications/fn_Statement/{id}/{voucherNumber}")]
        public Response<Integrations.RootObject> fn_Statement(long id, string voucherNumber)
        {
            try
            {
                log.Info(string.Format("fn_Statement for application {0}", id));
                Atlas.ThirdParty.NuPay.Models.NuCardDetails loadNuCard = new Atlas.ThirdParty.NuPay.Models.NuCardDetails()
                {
                    userId = Convert.ToString(ConfigurationManager.AppSettings["userid"]),
                    userPassword = Convert.ToString(ConfigurationManager.AppSettings["password"]),
                    terminalID = Convert.ToString(ConfigurationManager.AppSettings["terminal"]),
                    profileNumber = Convert.ToString(ConfigurationManager.AppSettings["profile"]),
                    voucherNumber = voucherNumber
                };
                var serviceResult = new Atlas.ThirdParty.NuPay.Integrations.NuCardIntegration().Statement(loadNuCard);
                var json = JsonConvert.SerializeXmlNode(serviceResult);
                var results = JsonConvert.DeserializeObject<IssueCardResponse>(json);
                if (results.response.tutukaResponse != null)
                {
                    try
                    {
                        results.response.tutukaResponse = results.response.tutukaResponse.Replace("<?xml version=\"1.0\" encoding=\"UTF-8\"?>", "");
                        System.Xml.XmlDocument doc = new System.Xml.XmlDocument(); doc.LoadXml(results.response.tutukaResponse);
                        var jsonObj = JsonConvert.DeserializeObject<Integrations.RootObject>(JsonConvert.SerializeXmlNode(doc));
                        return Response<Integrations.RootObject>.CreateResponse(Constants.Success, jsonObj, null);
                    }
                    catch (Exception ex)
                    {
                        log.Error(string.Format("[fn_Statement] Error parsing tutukaResponse for application {0}\nError: {1}", id, ex));
                        throw;
                    }
                }
                else
                {
                    log.Info(string.Format("service error while executing [fn_Statement] for application {0}. Error {1}", id, results.response.errorMessage));
                    return Response<Integrations.RootObject>.CreateResponse(Constants.Failed, null, new ErrorHandler() { ErrorCode = results.response.errorCode, Message = results.response.errorMessage });
                }
            }
            catch (Exception ex)
            {
                log.Error(string.Format("Error executing fn_Statement for application {0}\nError: {1}", id, ex));
                return Response<Integrations.RootObject>.CreateResponse(Constants.Failed, null, new ErrorHandler() { ErrorCode = Constants.ApplicationErrorCode, Message = Constants.FnStatementErrorMessage });
            }
        }

        [NonAction]
        public Response<Integrations.RootObject> fn_StopCard(long id, string voucherNumber, string ReasonId)
        {
            try
            {
                Atlas.ThirdParty.NuPay.Models.NuCardDetails nuCardDetails = new Atlas.ThirdParty.NuPay.Models.NuCardDetails()
                {
                    userId = Convert.ToString(ConfigurationManager.AppSettings["userid"]),
                    userPassword = Convert.ToString(ConfigurationManager.AppSettings["password"]),
                    terminalID = Convert.ToString(ConfigurationManager.AppSettings["terminal"]),
                    profileNumber = Convert.ToString(ConfigurationManager.AppSettings["profile"]),
                    voucherNumber = voucherNumber,
                    ReasonId = ReasonId
                };
                var serviceResult = new Atlas.ThirdParty.NuPay.Integrations.NuCardIntegration().StopCard(nuCardDetails);
                var json = JsonConvert.SerializeXmlNode(serviceResult);
                var results = JsonConvert.DeserializeObject<Integrations.IssueCardResponse>(json);
                if (results.response.tutukaResponse != null)
                {
                    try
                    {
                        results.response.tutukaResponse = results.response.tutukaResponse.Replace("<?xml version=\"1.0\" encoding=\"UTF-8\"?>", "");
                        System.Xml.XmlDocument doc = new System.Xml.XmlDocument(); doc.LoadXml(results.response.tutukaResponse);
                        var jsonObj = JsonConvert.DeserializeObject<Integrations.RootObject>(JsonConvert.SerializeXmlNode(doc));
                        return Response<Integrations.RootObject>.CreateResponse(Constants.Success, jsonObj, null);
                    }
                    catch (Exception ex)
                    {
                        log.Error(string.Format("[fn_StopCard] Error parsing tutukaResponse for application {0}\nError: {1}", id, ex));
                        throw;
                    }
                }
                else
                {
                    log.Info(string.Format("service error while executing [fn_Statement] for application {0}. Error {1}", id, results.response.errorMessage));
                    return Response<Integrations.RootObject>.CreateResponse(Constants.Failed, null, new ErrorHandler() { ErrorCode = results.response.errorCode, Message = results.response.errorMessage });
                }
            }
            catch (Exception ex)
            {
                log.Error(string.Format("Error executing fn_StopCard for application {0}\nError: {1}", id, ex));
                return Response<Integrations.RootObject>.CreateResponse(Constants.Failed, null, new ErrorHandler() { ErrorCode = Constants.ApplicationErrorCode, Message = Constants.StopCardErrorMessage });
            }
        }

        [NonAction]
        public Response<IssueCardResponse> CancelStopCard_CS(long id, string voucherNumber)
        {
            try
            {
                Atlas.ThirdParty.NuPay.Models.NuCardDetails nuCardDetails = new Atlas.ThirdParty.NuPay.Models.NuCardDetails()
                {
                    userId = Convert.ToString(ConfigurationManager.AppSettings["userid"]),
                    userPassword = Convert.ToString(ConfigurationManager.AppSettings["password"]),
                    terminalID = Convert.ToString(ConfigurationManager.AppSettings["terminal"]),
                    profileNumber = Convert.ToString(ConfigurationManager.AppSettings["profile"]),
                    voucherNumber = voucherNumber
                };
                var serviceResult = new Atlas.ThirdParty.NuPay.Integrations.NuCardIntegration().CancelStopCard_CS(nuCardDetails);
                var json = JsonConvert.SerializeXmlNode(serviceResult);
                var results = JsonConvert.DeserializeObject<IssueCardResponse>(json);
                return Response<IssueCardResponse>.CreateResponse(Constants.Success, results, null);
            }
            catch (Exception ex)
            {
                log.Error(string.Format("Error executing CancelStopCard_CS for application {0}\nError: {1}", id, ex));
                return Response<Integrations.IssueCardResponse>.CreateResponse(Constants.Failed, null, new ErrorHandler() { ErrorCode = Constants.ApplicationErrorCode, Message = Constants.CancelStopCardErrorMessage });
            }
        }

        [NonAction]
        public string generatequotation(GenerateQuotation quotation, int applicationId, out string genValidation)
        {
            try
            {
                string res = null;
                string validation = string.Empty;

                using (var result = new BackOfficeWebServiceClient("BackOfficeWebServer.NET"))
                {
                    res = result.GenerateQuotation(quotation, applicationId, out validation);
                    genValidation = validation;
                }
                return res;
            }
            catch (Exception ex)
            {
                log.Error(string.Format("Error generating quotation for application {0}\nError: {1}", applicationId, ex));
                throw;
            }
        }

        [HttpPost]
        [Route("Applications/View/{ViewName}")]
        public Response<List<VMApplication>> GetAllApplicationView(string viewName, [FromBody]Condition condition)
        {
            try
            {
                log.Info(string.Format("Get application for view: {0}", viewName));
                var role = this.HasAccess(BO_Object.Applications, BackOfficeEnum.Action.VIEW);
                List<VMApplication> applicationList = new List<VMApplication>();

                if (role.Status == Constants.Success)
                {
                    using (var result = new BackOfficeWebServiceClient("BackOfficeWebServer.NET"))
                    {
                        int branchId = Convert.ToInt32(HttpContext.Current.Session["BranchId"]);
                        applicationList = DBManager.GetApplicationView(viewName, branchId, ref condition);

                        if (applicationList == null)
                        {
                            log.Info(string.Format("Applications not found for view: {0}", viewName));
                            return Response<List<VMApplication>>.CreateResponse(Constants.Failed, null, new ErrorHandler() { ErrorCode = Constants.ApplicationErrorCode, Message = Constants.ApplicationsNotFoundErrorMessage });
                        }
                        else
                        {
                            log.Info(string.Format("{0} Applications found for view: {1}", applicationList.Count, viewName));
                            foreach (var temp in applicationList)
                            {
                                temp.DetailsUrl = "/Application/" + temp.ApplicationId;
                                temp.ActionsUrl = "/Applications/" + temp.ApplicationId + "/Actions";
                            }
                            return Response<List<VMApplication>>.CreateResponse(Constants.Success, applicationList, null, null, condition);
                        }
                    }
                }
                else
                {
                    log.Info(string.Format("Authentication error. {0}", role.Error));
                    return Response<List<VMApplication>>.CreateResponse(Constants.Failed, null, role.Error);
                }
            }
            catch (Exception ex)
            {
                log.Error(string.Format("Error getting application list. ViewName {0}\nError: {1}", viewName, ex));
                return Response<List<VMApplication>>.CreateResponse(Constants.Failed, null, new ErrorHandler() { ErrorCode = Constants.ApplicationErrorCode, Message = Constants.GetApplicationsErrorMessage });
            }
        }


        #region Stand Alone Loan Term Calculation
        [HttpPost]
        [Route("applications/StandAlonePossibleLoanTermCalculation")]
        public Response<List<dynamic>> StandAloneGetPossibleLoanTermCalculation(VMStandAlonePossibleLoanTermCalculation loanTermCalculation)
        {

            try
            {
                log.Info(string.Format("Get loan terms for application {0}, loan amount {1}, discretion {2}, frequency type {3}, VAP {4}, Insurance {5}, loan date {6}", loanTermCalculation.applicationId, loanTermCalculation.loanAmount, loanTermCalculation.DiscretionAmount, loanTermCalculation.frequencyTypeId, loanTermCalculation.IsVAPChecked, loanTermCalculation.IsInsuranceRequired, loanTermCalculation.LoanDate));
                List<dynamic> lst = new List<dynamic>();
                if (!ValidateToken())
                {
                    log.Info(string.Format("Invalid session token."));
                    return Response<List<dynamic>>.CreateResponse(Constants.Failed, lst, new ErrorHandler
                    {
                        ErrorCode = Constants.FailedCode,
                        Message = Constants.SessionTokenInvalid
                    });
                }
                //int[] period = { 1, 2, 3, 4, 5, 6, 12 };
                int periodLength = 1;
                if (loanTermCalculation.frequencyTypeId == (int)SalaryFrequency.Monthly)
                    periodLength = 12;
                if (loanTermCalculation.frequencyTypeId == (int)SalaryFrequency.Fortnightly)
                    periodLength = 26;
                if (loanTermCalculation.frequencyTypeId == (int)SalaryFrequency.Weekly)
                    periodLength = 52;
                int[] period = new int[periodLength];
                for (int i = 1; i <= periodLength; i++)
                {
                    period[i - 1] = i;
                }
                var loanDate = string.IsNullOrEmpty(loanTermCalculation.LoanDate) ? System.DateTime.UtcNow : Convert.ToDateTime(loanTermCalculation.LoanDate);

                loanTermCalculation.applicationId = 0;
                try
                {

                    int annualRateOfIntrest = loanTermCalculation.RateofInterest * 12;

                    int payDay = 0;

                    long? branchId = Convert.ToInt64(HttpContext.Current.Session["BranchId"]);
                    if (branchId == null)
                    {

                    }
                    using (var result = new BackOfficeWebServiceClient("BackOfficeWebServer.NET"))
                    {
                        try
                        {
                            log.Info(string.Format("Get application {0} details", loanTermCalculation.applicationId));

                            var filterConditions = FilterUtil.GetFiltersAsString(BO_Object.Applications, Convert.ToInt32(HttpContext.Current.Session["UserId"]));
                            ApplicationDto applicationDetails = null;
                            payDay = applicationDetails != null ?
                                applicationDetails.Employer != null ? applicationDetails.Employer.PayDay : 0 : 0;
                        }
                        catch (Exception ex)
                        {
                            log.Error(string.Format("Error getting application {0} details\nError: {1}", loanTermCalculation.applicationId, ex));
                            throw;
                        }
                    }

                    #region ATLASONL-901
                    //if first repayment date and last repayment date is null only then calculate both the dates and functionality as it is
                    if (loanTermCalculation.FirstRepaymentDate == null && loanTermCalculation.LastRepaymentDate == null)
                    {
                        DateTime? firstRepaymentDate = GetFirstRepaymentDateByRules(loanDate, loanTermCalculation.frequencyTypeId, payDay);

                        foreach (var term in period)
                        {
                            try
                            {
                                DateTime? lastRepaymentDate = GetLastRepaymentDateByRules(loanDate, firstRepaymentDate.Value, loanTermCalculation.frequencyTypeId, payDay, term);
                                var obj = GetCalculatedObjectByRules(loanTermCalculation.loanAmount, loanDate, firstRepaymentDate.Value, lastRepaymentDate.Value, loanTermCalculation.DiscretionAmount, term, loanTermCalculation.frequencyTypeId, loanTermCalculation.applicationId, loanTermCalculation.IsVAPChecked, loanTermCalculation.IsInsuranceRequired, annualRateOfIntrest, payDay, branchId);

                                lst.Add(obj);

                            }
                            catch (Exception ex)
                            {
                                throw;
                            }
                        }
                    }
                    else if (loanTermCalculation.FirstRepaymentDate != null && loanTermCalculation.LastRepaymentDate == null)
                    {
                        var validDate = loanDate.AddDays(3);

                        if (loanTermCalculation.FirstRepaymentDate.Value >= validDate)
                        {
                            foreach (var term in period)
                            {
                                try
                                {
                                    DateTime? lastRepaymentDate = GetCalculatedLastRepaymentDate(loanDate, loanTermCalculation.FirstRepaymentDate.Value, loanTermCalculation.frequencyTypeId, payDay, term);
                                    var obj = GetCalculatedObjectByRules(loanTermCalculation.loanAmount, loanDate, loanTermCalculation.FirstRepaymentDate.Value, lastRepaymentDate.Value, loanTermCalculation.DiscretionAmount, term, loanTermCalculation.frequencyTypeId, loanTermCalculation.applicationId, loanTermCalculation.IsVAPChecked, loanTermCalculation.IsInsuranceRequired, annualRateOfIntrest, payDay, branchId);

                                    lst.Add(obj);

                                }
                                catch (Exception ex)
                                {
                                    throw;
                                }
                            }
                        }
                        else
                        {
                            log.Info(string.Format("Invalid first repayment date : {0} , for application : {1}", loanTermCalculation.FirstRepaymentDate.Value, loanTermCalculation.applicationId));
                            return Response<List<dynamic>>.CreateResponse(Constants.Failed, lst, new ErrorHandler() { ErrorCode = Constants.InvalidFirstRepaymentDateErrorCode, Message = Constants.InvalidFirstRepaymentDateErrorMessage });
                        }
                    }
                    else if (loanTermCalculation.LastRepaymentDate != null)
                    {
                        var dates = GetCalculatedFirstRepaymentDate(loanDate, loanTermCalculation.LastRepaymentDate.Value, loanTermCalculation.frequencyTypeId, payDay);
                        if (dates != null && dates.Count > 0)
                        {
                            var validDate = loanTermCalculation.frequencyTypeId == (int)SalaryFrequency.Monthly ? loanDate.AddDays(60) :
                                loanTermCalculation.frequencyTypeId == (int)SalaryFrequency.Fortnightly ? loanDate.AddDays(28) : loanDate.AddDays(14);

                            for (int i = 0; i < dates.Count; i++)
                            {
                                if (dates[i] < validDate)
                                {
                                    var terms = dates.Where((x, idx) => idx >= i).ToArray();
                                    int termCount = terms.Count();
                                    var obj = GetCalculatedObjectByRules(loanTermCalculation.loanAmount, loanDate, terms.FirstOrDefault(), loanTermCalculation.LastRepaymentDate.Value, loanTermCalculation.DiscretionAmount, termCount, loanTermCalculation.frequencyTypeId, loanTermCalculation.applicationId, loanTermCalculation.IsVAPChecked, loanTermCalculation.IsInsuranceRequired, annualRateOfIntrest, payDay, branchId);

                                    lst.Add(obj);

                                }
                            }
                            if (lst != null)
                            {
                                lst.OrderBy(x => x.Term);
                            }
                        }
                    }
                    //if first repayment date is not null and last repayment date is null then calculate last repayment date functionality as it is
                    //if first repayment date is null and last repaymnt date is not null then calcylate first repayment date and calculate possible number of terms and then functionality as it is
                    #endregion
                }
                catch (Exception ex)
                {
                    log.Error(string.Format("Error calculating interest rate for application {0}, loan date {1}\nError: {2}", loanTermCalculation.applicationId, loanTermCalculation.LoanDate, ex));
                    throw;
                }
                if (lst.Count() == 0)
                {
                    log.Info(string.Format("Discretion amount is insufficient for application {0}.", loanTermCalculation.applicationId));
                    return Response<List<dynamic>>.CreateResponse(Constants.Failed, lst, new ErrorHandler() { ErrorCode = Constants.ApplicationErrorCode, Message = Constants.LoanCalculationError });
                }
                return Response<List<dynamic>>.CreateResponse(Constants.Success, lst, new ErrorHandler() { });
            }
            catch (Exception ex)
            {
                log.Error(string.Format("Error getting loan terms for application {0}, loan amount {1}, discretion {2}, frequency type {3}, VAP {4}, Insurance {5}, loan date {6}\nError: {7}", loanTermCalculation.applicationId, loanTermCalculation.loanAmount, loanTermCalculation.DiscretionAmount, loanTermCalculation.frequencyTypeId, loanTermCalculation.IsVAPChecked, loanTermCalculation.IsInsuranceRequired, loanTermCalculation.LoanDate, ex));

                return Response<List<dynamic>>.CreateResponse(Constants.Failed, null, new ErrorHandler() { ErrorCode = Constants.ApplicationErrorCode, Message = Constants.LoanTermsCalculationErrorMessage });
            }
        }
        #endregion


        [HttpPost]
        [Route("applications/PossibleLoanTermCalculation")]
        public Response<List<dynamic>> GetPossibleLoanTermCalculation(VMPossibleLoanTermCalculation loanTermCalculation)
        {
            try
            {
                log.Info(string.Format("Get loan terms for application {0}, loan amount {1}, discretion {2}, frequency type {3}, VAP {4}, Insurance {5}, loan date {6}", loanTermCalculation.applicationId, loanTermCalculation.loanAmount, loanTermCalculation.DiscretionAmount, loanTermCalculation.frequencyTypeId, loanTermCalculation.IsVAPChecked, loanTermCalculation.IsInsuranceRequired, loanTermCalculation.LoanDate));

                //int[] period = { 1, 2, 3, 4, 5, 6, 12 };
                int periodLength = 1;
                if (loanTermCalculation.frequencyTypeId == (int)SalaryFrequency.Monthly)
                    periodLength = 12;
                if (loanTermCalculation.frequencyTypeId == (int)SalaryFrequency.Fortnightly)
                    periodLength = 26;
                if (loanTermCalculation.frequencyTypeId == (int)SalaryFrequency.Weekly)
                    periodLength = 52;
                int[] period = new int[periodLength];
                for (int i = 1; i <= periodLength; i++)
                {
                    period[i - 1] = i;
                }
                var loanDate = string.IsNullOrEmpty(loanTermCalculation.LoanDate) ? System.DateTime.UtcNow : Convert.ToDateTime(loanTermCalculation.LoanDate);

                List<dynamic> lst = new List<dynamic>();
                long? branchId = 0;

                try
                {
                    int annualRateOfIntrest = DBManager.GetInterestRate(loanTermCalculation.applicationId, loanDate);

                    int payDay = 0;
                    using (var result = new BackOfficeWebServiceClient("BackOfficeWebServer.NET"))
                    {
                        try
                        {
                            log.Info(string.Format("Get application {0} details", loanTermCalculation.applicationId));

                            var filterConditions = FilterUtil.GetFiltersAsString(BO_Object.Applications, Convert.ToInt32(HttpContext.Current.Session["UserId"]));
                            ApplicationDto applicationDetails = result.GetApplicationDetail(filterConditions, loanTermCalculation.applicationId);
                            payDay = applicationDetails != null ?
                                applicationDetails.Employer != null ? applicationDetails.Employer.PayDay : 0 : 0;
                            branchId = applicationDetails.ApplicationClient.Branch.BranchId;
                        }
                        catch (Exception ex)
                        {
                            log.Error(string.Format("Error getting application {0} details\nError: {1}", loanTermCalculation.applicationId, ex));
                            throw;
                        }
                    }

                    #region ATLASONL-901
                    //if first repayment date and last repayment date is null only then calculate both the dates and functionality as it is
                    if (loanTermCalculation.FirstRepaymentDate == null && loanTermCalculation.LastRepaymentDate == null)
                    {
                        DateTime? firstRepaymentDate = GetFirstRepaymentDateByRules(loanDate, loanTermCalculation.frequencyTypeId, payDay);

                        foreach (var term in period)
                        {
                            try
                            {
                                DateTime? lastRepaymentDate = GetLastRepaymentDateByRules(loanDate, firstRepaymentDate.Value, loanTermCalculation.frequencyTypeId, payDay, term);
                                var obj = GetCalculatedObjectByRules(loanTermCalculation.loanAmount, loanDate, firstRepaymentDate.Value, lastRepaymentDate.Value, loanTermCalculation.DiscretionAmount, term, loanTermCalculation.frequencyTypeId, loanTermCalculation.applicationId, loanTermCalculation.IsVAPChecked, loanTermCalculation.IsInsuranceRequired, annualRateOfIntrest, payDay, branchId);
                                if (obj.Surplus >= 0)
                                {
                                    lst.Add(obj);
                                }
                            }
                            catch (Exception ex)
                            {
                                throw;
                            }
                        }
                    }
                    else if (loanTermCalculation.FirstRepaymentDate != null && loanTermCalculation.LastRepaymentDate == null)
                    {
                        var validDate = loanDate.AddDays(3);
                        int days = GetWorkingDays(loanDate, loanTermCalculation.FirstRepaymentDate.Value);
                        //if (loanTermCalculation.FirstRepaymentDate.Value >= validDate && ValidatePayDay(payDay, loanTermCalculation.FirstRepaymentDate.Value, loanTermCalculation.frequencyTypeId))
                        if (days >= 3 && ValidatePayDay(payDay, loanTermCalculation.FirstRepaymentDate.Value, loanTermCalculation.frequencyTypeId))
                        {
                            foreach (var term in period)
                            {
                                try
                                {
                                    DateTime? lastRepaymentDate = GetCalculatedLastRepaymentDate(loanDate, loanTermCalculation.FirstRepaymentDate.Value, loanTermCalculation.frequencyTypeId, payDay, term);
                                    var obj = GetCalculatedObjectByRules(loanTermCalculation.loanAmount, loanDate, loanTermCalculation.FirstRepaymentDate.Value, lastRepaymentDate.Value, loanTermCalculation.DiscretionAmount, term, loanTermCalculation.frequencyTypeId, loanTermCalculation.applicationId, loanTermCalculation.IsVAPChecked, loanTermCalculation.IsInsuranceRequired, annualRateOfIntrest, payDay, branchId);
                                    if (obj.Surplus >= 0)
                                    {
                                        lst.Add(obj);
                                    }
                                }
                                catch (Exception ex)
                                {
                                    log.Error(ex);
                                    throw;
                                }
                            }
                        }
                        else
                        {
                            log.Info(string.Format("Invalid first repayment date : {0} , for application : {1}", loanTermCalculation.FirstRepaymentDate.Value, loanTermCalculation.applicationId));
                            return Response<List<dynamic>>.CreateResponse(Constants.Failed, lst, new ErrorHandler() { ErrorCode = Constants.InvalidFirstRepaymentDateErrorCode, Message = Constants.InvalidFirstRepaymentDateErrorMessage });
                        }
                    }
                    else if (loanTermCalculation.LastRepaymentDate != null && ValidatePayDay(payDay, loanTermCalculation.LastRepaymentDate.Value, loanTermCalculation.frequencyTypeId))
                    {
                        var dates = GetCalculatedFirstRepaymentDate(loanDate, loanTermCalculation.LastRepaymentDate.Value, loanTermCalculation.frequencyTypeId, payDay);
                        if (dates != null && dates.Count > 0)
                        {
                            var validDate = loanTermCalculation.frequencyTypeId == (int)SalaryFrequency.Monthly ? loanDate.AddDays(60) :
                                loanTermCalculation.frequencyTypeId == (int)SalaryFrequency.Fortnightly ? loanDate.AddDays(28) : loanDate.AddDays(14);

                            for (int i = 0; i < dates.Count; i++)
                            {
                                if (dates[i] < validDate)
                                {
                                    var terms = dates.Where((x, idx) => idx >= i).ToArray();
                                    int termCount = terms.Count();
                                    var obj = GetCalculatedObjectByRules(loanTermCalculation.loanAmount, loanDate, terms.FirstOrDefault(), loanTermCalculation.LastRepaymentDate.Value, loanTermCalculation.DiscretionAmount, termCount, loanTermCalculation.frequencyTypeId, loanTermCalculation.applicationId, loanTermCalculation.IsVAPChecked, loanTermCalculation.IsInsuranceRequired, annualRateOfIntrest, payDay, branchId);
                                    if (obj.Surplus >= 0)
                                    {
                                        lst.Add(obj);
                                    }
                                }
                            }
                            if (lst != null)
                            {
                                lst.OrderBy(x => x.Term);
                            }
                        }
                    }
                    //if first repayment date is not null and last repayment date is null then calculate last repayment date functionality as it is
                    //if first repayment date is null and last repaymnt date is not null then calcylate first repayment date and calculate possible number of terms and then functionality as it is
                    #endregion

                }
                catch (Exception ex)
                {
                    log.Error(string.Format("Error calculating interest rate for application {0}, loan date {1}\nError: {2}", loanTermCalculation.applicationId, loanTermCalculation.LoanDate, ex));
                    throw;
                }
                if (lst.Count() == 0)
                {
                    log.Info(string.Format("Discretion amount is insufficient for application {0}.", loanTermCalculation.applicationId));
                    return Response<List<dynamic>>.CreateResponse(Constants.Failed, lst, new ErrorHandler() { ErrorCode = Constants.ApplicationErrorCode, Message = Constants.LoanCalculationError });
                }
                return Response<List<dynamic>>.CreateResponse(Constants.Success, lst, new ErrorHandler() { });
            }
            catch (Exception ex)
            {
                log.Error(string.Format("Error getting loan terms for application {0}, loan amount {1}, discretion {2}, frequency type {3}, VAP {4}, Insurance {5}, loan date {6}\nError: {7}", loanTermCalculation.applicationId, loanTermCalculation.loanAmount, loanTermCalculation.DiscretionAmount, loanTermCalculation.frequencyTypeId, loanTermCalculation.IsVAPChecked, loanTermCalculation.IsInsuranceRequired, loanTermCalculation.LoanDate, ex));

                return Response<List<dynamic>>.CreateResponse(Constants.Failed, null, new ErrorHandler() { ErrorCode = Constants.ApplicationErrorCode, Message = Constants.LoanTermsCalculationErrorMessage });
            }
        }

        [NonAction]
        private dynamic GetCalculatedObject(decimal loanAmount, DateTime loanDate, DateTime loanEndDate, decimal discretionAmt, int period, int frequencyType, int applicationId, bool isVAPChecked, bool isInsuranceRequired, int annualRateOfIntrest, int payDay, long? branchId)
        {
            try
            {
                log.Info(string.Format("Get loan terms for application {0}, loan amount {1}, discretion {2}, period {3}, VAP {4}, Insurance {5}, interest rate {6}, pay day {7}", applicationId, loanAmount, discretionAmt, period, isVAPChecked, isInsuranceRequired, annualRateOfIntrest, payDay));
                int daysInFirstInstallment = 0;
                var daysInOneTerm = 30;
                var daysPerMonth = 30;
                var monthsInYear = 12;
                var frequency = 12;
                decimal serviceFeesPM = 60;
                decimal vatRate = 15;
                decimal quantityFrequencyInAMonth = 1;
                var firstRepayDate = loanDate.AddDays(3);
                var RepaymentDate = loanDate.AddDays(3);
                bool isAdditionalDaysInFirstInstallment = false;
                if (frequencyType == (int)SalaryFrequency.Monthly)
                {
                    try
                    {
                        log.Info(string.Format("Calculating firstRepayDate and RepaymentDate for monthly frequecy"));
                        frequency = 12;
                        daysInOneTerm = 30;
                        quantityFrequencyInAMonth = 1;

                        DateTime paydayca = new DateTime(loanDate.Year, loanDate.Month, loanDate.Day).AddDays(15);
                        DateTime payDate = new DateTime();
                        try
                        {
                            payDate = new DateTime(loanDate.Year, loanDate.Month, payDay);
                        }
                        catch (Exception ex)
                        {
                            payDate = new DateTime(loanDate.Year, loanDate.Month, DateTime.DaysInMonth(loanDate.Year, loanDate.Month));
                        }
                        if (payDate <= paydayca)
                        {
                            firstRepayDate = payDate.AddMonths(1);
                            if ((firstRepayDate - loanDate).Days < 15)
                            {
                                firstRepayDate = firstRepayDate.AddMonths(1);
                            }
                        }
                        else
                        {
                            firstRepayDate = payDate;
                        }
                        if (payDay > firstRepayDate.Day)
                        {
                            RepaymentDate = firstRepayDate.AddMonths((period - 1));
                            try
                            {
                                RepaymentDate = new DateTime(RepaymentDate.Year, RepaymentDate.Month, payDay);
                            }
                            catch (Exception ex)
                            {
                                log.Error(ex);
                                RepaymentDate = new DateTime(RepaymentDate.Year, RepaymentDate.Month, DateTime.DaysInMonth(RepaymentDate.Year, RepaymentDate.Month));
                            }
                        }
                        else
                        {
                            RepaymentDate = firstRepayDate.AddMonths((period - 1));
                        }
                    }
                    catch (Exception ex)
                    {
                        log.Error(string.Format("Error calculating firstRepayDate and RepaymentDate for monthly frequecy\nError: {0}", ex));
                        throw;
                    }
                }
                if (frequencyType == (int)SalaryFrequency.Fortnightly)
                {
                    try
                    {
                        log.Info(string.Format("Calculating firstRepayDate and RepaymentDate for Fortnightly frequecy"));
                        frequency = 26;
                        daysInOneTerm = 14;
                        quantityFrequencyInAMonth = 2;
                        DateTime paydayca = new DateTime(loanDate.Year, loanDate.Month, loanDate.Day).AddDays(7);
                        if ((new DateTime(loanDate.Year, loanDate.Month, payDay)) <= paydayca)
                        {
                            firstRepayDate = new DateTime(loanDate.Year, loanDate.Month, payDay).AddDays(14);
                            if ((firstRepayDate - loanDate).Days < 7)
                            {
                                firstRepayDate = firstRepayDate.AddDays(14);
                            }
                            if ((firstRepayDate - loanDate).Days < 7)
                            {
                                firstRepayDate = firstRepayDate.AddDays(14);
                            }
                        }
                        else
                        {
                            firstRepayDate = new DateTime(loanDate.Year, loanDate.Month, payDay);
                        }
                        RepaymentDate = firstRepayDate.AddDays(14 * (period - 1));
                    }
                    catch (Exception ex)
                    {
                        log.Error(string.Format("Error calculating firstRepayDate and RepaymentDate for Fortnightly frequecy\nError: {0}", ex));
                        throw;
                    }
                }
                if (frequencyType == (int)SalaryFrequency.Weekly)
                {
                    try
                    {
                        log.Info(string.Format("Calculating firstRepayDate and RepaymentDate for Weekly frequecy"));
                        int additionalDays = 0;
                        if ((int)firstRepayDate.DayOfWeek > payDay)
                            additionalDays = 7 - ((int)firstRepayDate.DayOfWeek - payDay);
                        else
                            additionalDays = payDay - (int)firstRepayDate.DayOfWeek;

                        log.Info("additionalDays: " + additionalDays);
                        frequency = 52;
                        daysInOneTerm = 7;
                        quantityFrequencyInAMonth = 4;
                        firstRepayDate = firstRepayDate.AddDays(additionalDays);
                        RepaymentDate = RepaymentDate.AddDays(daysInOneTerm * (period - 1)).AddDays(additionalDays);
                        int workingDays = GetWorkingDays(loanDate, firstRepayDate);
                        log.Info("working days: " + workingDays);
                        if (workingDays < 3)
                        {
                            firstRepayDate = firstRepayDate.AddDays(7);
                            log.Info("First repay date after working days calc: " + firstRepayDate);
                            RepaymentDate = RepaymentDate.AddDays(7);
                            log.Info("repay date: " + RepaymentDate);
                            daysInFirstInstallment = (firstRepayDate - loanDate).Days + 1;
                            isAdditionalDaysInFirstInstallment = true;
                        }
                        log.Info("Last Repay date after working days calc: " + RepaymentDate);
                    }
                    catch (Exception ex)
                    {
                        log.Error(string.Format("Error calculating firstRepayDate and RepaymentDate for Weekly frequecy\nError: {0}", ex));
                        throw;
                    }
                }

                loanEndDate = RepaymentDate;
                var deferredAmount = loanAmount + CalculateInitiationFee(loanAmount);
                var creditProtectionRatePM = 0.55;
                var daysInLoan = (int)Math.Ceiling((loanEndDate - loanDate).TotalDays + 1);
                var quantityInstallments = Math.Round((decimal)daysInLoan / daysInOneTerm, 0);
                if (isAdditionalDaysInFirstInstallment)
                    quantityInstallments = quantityInstallments - 1;

                var qtyMonthsPerMonthlyTerm = (quantityInstallments / quantityFrequencyInAMonth);
                var quantityMonthsRoundForInsurance = (int)Math.Round(qtyMonthsPerMonthlyTerm, 0, MidpointRounding.AwayFromZero);

                DateTime beginingOfFirstMonth = new DateTime(loanDate.Year, loanDate.Month, 1);
                int daysInFirstMonth = DateTime.DaysInMonth(loanDate.Year, loanDate.Month);
                DateTime endOfFirstMonth = new DateTime(loanDate.Year, loanDate.Month, daysInFirstMonth);
                int tempVal = endOfFirstMonth > loanEndDate ? (loanEndDate - loanDate).Days + 1 : (endOfFirstMonth - loanDate).Days + 1;
                if (tempVal > 30)
                {
                    daysInFirstMonth = 30;
                }
                else
                {
                    if (endOfFirstMonth > loanEndDate)
                    {
                        daysInFirstMonth = (loanEndDate - loanDate).Days + 1;
                    }
                    else
                    {
                        daysInFirstMonth = (endOfFirstMonth - loanDate).Days + 1;
                    }
                }

                int quantityMonthsAfterFirstMonth = (((loanEndDate.Year - endOfFirstMonth.AddMonths(1).Year) * 12) + loanEndDate.Month - endOfFirstMonth.AddMonths(1).Month) + 1;

                var proRataDaysInFirstMonth = 0;

                if (loanDate == beginingOfFirstMonth && loanEndDate >= endOfFirstMonth)
                { proRataDaysInFirstMonth = (endOfFirstMonth - loanDate).Days + 1; }
                else
                {
                    if (endOfFirstMonth >= loanEndDate)
                    {
                        proRataDaysInFirstMonth = (loanEndDate - loanDate).Days + 1;
                    }
                    else
                    {
                        proRataDaysInFirstMonth = (endOfFirstMonth - loanDate).Days + 1;
                    }
                }

                daysInFirstInstallment = daysInFirstInstallment > 0 ? daysInFirstInstallment : Convert.ToInt32(daysInLoan - daysInOneTerm * (quantityInstallments - 1));

                var intrest = (deferredAmount * annualRateOfIntrest / monthsInYear) / 100 * daysInFirstInstallment / daysPerMonth;
                var intrestFirstInstallment = (deferredAmount * annualRateOfIntrest / monthsInYear) / 100 * daysInFirstInstallment / daysPerMonth;

                var percent = (double)annualRateOfIntrest / frequency / 100;
                log.Info($"Total Intrest calculation : Period: {period} PMT function values Rate : {percent}, NPer : {(double)quantityInstallments}, PV : {-(double)(deferredAmount + intrestFirstInstallment)}, FV: 0, DueDate: BegOfPeriod");
                log.Info($"Pmt Value : {(decimal)Microsoft.VisualBasic.Financial.Pmt(percent, (double)quantityInstallments, -(double)(deferredAmount + intrestFirstInstallment), 0, Microsoft.VisualBasic.DueDate.BegOfPeriod) }");
                var intrestTableA = ((decimal)Microsoft.VisualBasic.Financial.Pmt(percent, (double)quantityInstallments, -(double)(deferredAmount + intrestFirstInstallment), 0, Microsoft.VisualBasic.DueDate.BegOfPeriod) * quantityInstallments) - (deferredAmount + intrestFirstInstallment);
                var totalIntrest = intrestFirstInstallment + intrestTableA;
                var daysInLoanMonth = DateTime.DaysInMonth(loanDate.Year, loanDate.Month);
                var totalServiceFees = CalculateServiceFees(serviceFeesPM, daysInFirstMonth, daysInLoanMonth, quantityMonthsAfterFirstMonth);
                //var totalServiceFees = 60;
                var serviceFeeVAT = totalServiceFees * vatRate / 100;
                var totalServiceFeesInclVAT = totalServiceFees + serviceFeeVAT;
                var subTotal = deferredAmount + totalIntrest + totalServiceFeesInclVAT;
                var creditLife = ((subTotal * (quantityInstallments + 1)) / 2) * ((decimal)creditProtectionRatePM / 100) * ((decimal)quantityMonthsRoundForInsurance / quantityInstallments);


                subTotal = isInsuranceRequired ? subTotal + creditLife : subTotal;
                var premium = isInsuranceRequired ? creditLife / quantityInstallments : 0;

                var intRatePerInstallment = (double)annualRateOfIntrest / frequency / 100;
                var tempVal1 = (double)Math.Pow((double)(1 + intRatePerInstallment), (double)-quantityInstallments);
                var tempVal2 = intRatePerInstallment / (1 - tempVal1);
                var tempVal3 = tempVal2 / (1 + intRatePerInstallment);

                decimal VAPAmt = 0;
                bool IsVAPApplied = false;
                if (isVAPChecked)
                {
                    try
                    {
                        log.Info("VAP calculations");
                        using (var result = new BackOfficeWebServiceClient("BackOfficeWebServer.NET"))
                        {
                            int CurrentappLoanPeriod = period * daysInOneTerm;
                            IsVAPApplied = result.IsVAPApplied(applicationId, CurrentappLoanPeriod);
                        }

                        int vapPeriodInMonths = period;

                        if (frequencyType == (int)SalaryFrequency.Fortnightly)
                        {
                            vapPeriodInMonths = (int)Math.Ceiling(Convert.ToDecimal(period) / Convert.ToDecimal(2));
                        }

                        if (frequencyType == (int)SalaryFrequency.Weekly)
                        {
                            vapPeriodInMonths = (int)Math.Ceiling(Convert.ToDecimal(period) / Convert.ToDecimal(4));
                        }

                        vapPeriodInMonths = vapPeriodInMonths > 12 ? 12 : vapPeriodInMonths;

                        VAPAmt = IsVAPApplied == true
                        ? CalculateVAPAmount(loanAmount, vapPeriodInMonths, (int)SalaryFrequency.Monthly, branchId, loanDate)
                         : 0;

                        var totalVAP = 0.0M;
                        if (VAPAmt != 0)
                        {
                            if (frequencyType == (int)SalaryFrequency.Fortnightly)
                            {
                                VAPAmt = VAPAmt / 2;
                                totalVAP = VAPAmt * quantityInstallments;
                            }
                            else if (frequencyType == (int)SalaryFrequency.Weekly)
                            {
                                VAPAmt = VAPAmt / 4;
                                totalVAP = VAPAmt * quantityInstallments;
                            }
                        }
                        else
                        {
                            log.Info("[GetCalculatedObject] VAP amount is zero for Application Id : " + applicationId);
                        }
                    }
                    catch (Exception ex)
                    {
                        log.Error(string.Format("Error calculating VAP. Error: {0}", ex));
                        throw;
                    }
                }

                var installment = (decimal)tempVal3 * (deferredAmount + intrest);

                var TotalInstallment = Math.Round(((totalServiceFeesInclVAT / period) + premium + installment + VAPAmt), 2);

                var discretionAmount = discretionAmt - TotalInstallment;

                var surplus = Math.Round(discretionAmt - (TotalInstallment * quantityFrequencyInAMonth), 2);
                if (frequencyType == (int)SalaryFrequency.Weekly)
                {
                    if (period < 4)
                        surplus = Math.Round(discretionAmt - (TotalInstallment * period), 2);
                }
                if (frequencyType == (int)SalaryFrequency.Fortnightly)
                {
                    if (period < 2)
                        surplus = Math.Round(discretionAmt - (TotalInstallment * period), 2);
                }

                return new
                {
                    Term = period,
                    DeferredAmount = Math.Round(deferredAmount, 2),
                    Interest = Math.Round(intrest, 2),
                    InitiationFee = CalculateInitiationFee(loanAmount),
                    ServiceFee = totalServiceFeesInclVAT,
                    Premium = Math.Round(premium, 2),
                    Installment = Math.Round(installment, 2),
                    TotalInstallment = Math.Round(TotalInstallment, 2),
                    LoanAmount = loanAmount,
                    RepaymentAmount = Math.Round(TotalInstallment * period, 2),
                    RepaymentDate = RepaymentDate,
                    Surplus = surplus,
                    RemaningBalance = (Math.Round(deferredAmount, 2) + Math.Round(intrest, 2) + totalServiceFeesInclVAT + Math.Round(premium, 2)) - TotalInstallment,
                    VAP = IsVAPApplied,
                    VAP_Amount = VAPAmt,
                    annualRateOfIntrest = annualRateOfIntrest,
                    QuantityInstallments = quantityInstallments,
                    TotalInterest = totalIntrest,
                    FirstRepayDate = firstRepayDate,
                    ServiceFeeAmount = totalServiceFees,
                    ServiceVATAmount = serviceFeeVAT
                };
            }
            catch (Exception ex)
            {
                log.Error(string.Format("[GetCalculatedObject] Error calculating loan terms for application {0}, loan amount {1}, discretion {2}, period {3}, VAP {4}, Insurance {5}, interest rate {6}, pay day {7}\nError: {8}", applicationId, loanAmount, discretionAmt, period, isVAPChecked, isInsuranceRequired, annualRateOfIntrest, payDay, ex));
                throw;
            }
        }

        [NonAction]
        private dynamic GetCalculatedObjectByRules(decimal loanAmount, DateTime loanDate, DateTime firstRepayDate, DateTime loanEndDate, decimal discretionAmt, int period, int frequencyType, int applicationId, bool isVAPChecked, bool isInsuranceRequired, int annualRateOfIntrest, int payDay, long? branchId)
        {
            try
            {
                log.Info(string.Format("Get loan terms for application {0}, loan amount {1}, discretion {2}, period {3}, VAP {4}, Insurance {5}, interest rate {6}, pay day {7}", applicationId, loanAmount, discretionAmt, period, isVAPChecked, isInsuranceRequired, annualRateOfIntrest, payDay));
                int daysInFirstInstallment = 0;
                var daysInOneTerm = 30;
                var daysPerMonth = 30;
                var monthsInYear = 12;
                var frequency = 12;
                decimal serviceFeesPM = 60;
                decimal vatRate = 15;
                decimal quantityFrequencyInAMonth = 1;

                bool isAdditionalDaysInFirstInstallment = false;

                if (frequencyType == (int)SalaryFrequency.Fortnightly)
                {
                    frequency = 26;
                    daysInOneTerm = 14;
                    quantityFrequencyInAMonth = 2;
                }

                if (frequencyType == (int)SalaryFrequency.Weekly)
                {
                    frequency = 52;
                    daysInOneTerm = 7;
                    quantityFrequencyInAMonth = 4;

                    int workingDays = GetWorkingDays(loanDate, firstRepayDate);
                    log.Info("working days: " + workingDays);
                    if (workingDays < 3)
                    {
                        daysInFirstInstallment = (firstRepayDate - loanDate).Days + 1;
                        isAdditionalDaysInFirstInstallment = true;
                    }
                }

                var deferredAmount = loanAmount + CalculateInitiationFee(loanAmount);
                var creditProtectionRatePM = 0.55;
                var daysInLoan = (int)Math.Ceiling((loanEndDate - loanDate).TotalDays + 1);
                var quantityInstallments = period;// Math.Round((decimal)daysInLoan / daysInOneTerm,0);
                quantityInstallments = quantityInstallments == 0 ? 1 : quantityInstallments; //Number of installments can not be never loan date : 14-01-2019 FirstReayDate : 25-01-2019
                if (isAdditionalDaysInFirstInstallment)
                    quantityInstallments = quantityInstallments - 1;

                // var quantityInstallments = Math.Floor((decimal)daysInLoan / daysInOneTerm) + 1;
                //var quantityInstallments = period;
                var qtyMonthsPerMonthlyTerm = (quantityInstallments / quantityFrequencyInAMonth);
                var quantityMonthsRoundForInsurance = (int)Math.Round(qtyMonthsPerMonthlyTerm, 0, MidpointRounding.AwayFromZero);

                DateTime beginingOfFirstMonth = new DateTime(loanDate.Year, loanDate.Month, 1);
                int daysInFirstMonth = DateTime.DaysInMonth(loanDate.Year, loanDate.Month);
                DateTime endOfFirstMonth = new DateTime(loanDate.Year, loanDate.Month, daysInFirstMonth);
                //daysInFirstMonth = (endOfFirstMonth - loanDate).Days + 1;
                int tempVal = endOfFirstMonth > loanEndDate ? (loanEndDate - loanDate).Days + 1 : (endOfFirstMonth - loanDate).Days + 1;
                if (tempVal > 30)
                {
                    daysInFirstMonth = 30;
                }
                else
                {
                    if (endOfFirstMonth > loanEndDate)
                    {
                        daysInFirstMonth = (loanEndDate - loanDate).Days + 1;
                    }
                    else
                    {
                        daysInFirstMonth = (endOfFirstMonth - loanDate).Days + 1;
                    }
                }

                int quantityMonthsAfterFirstMonth = (((loanEndDate.Year - endOfFirstMonth.AddMonths(1).Year) * 12) + loanEndDate.Month - endOfFirstMonth.AddMonths(1).Month) + 1;

                var proRataDaysInFirstMonth = 0;

                if (loanDate == beginingOfFirstMonth && loanEndDate >= endOfFirstMonth)
                { proRataDaysInFirstMonth = (endOfFirstMonth - loanDate).Days + 1; }
                else
                {
                    if (endOfFirstMonth >= loanEndDate)
                    {
                        proRataDaysInFirstMonth = (loanEndDate - loanDate).Days + 1;
                    }
                    else
                    {
                        proRataDaysInFirstMonth = (endOfFirstMonth - loanDate).Days + 1;
                    }
                }

                daysInFirstInstallment = daysInFirstInstallment > 0 ? daysInFirstInstallment : Convert.ToInt32(daysInLoan - daysInOneTerm * (quantityInstallments - 1));

                var intrest = (deferredAmount * annualRateOfIntrest / monthsInYear) / 100 * daysInFirstInstallment / daysPerMonth;
                var intrestFirstInstallment = (deferredAmount * annualRateOfIntrest / monthsInYear) / 100 * daysInFirstInstallment / daysPerMonth;

                var percent = (double)annualRateOfIntrest / frequency / 100;
                log.Info($"Total Intrest calculation : Period: {period} PMT function values Rate : {percent}, NPer : {(double)quantityInstallments}, PV : {-(double)(deferredAmount + intrestFirstInstallment)}, FV: 0, DueDate: BegOfPeriod");
                log.Info($"Pmt Value : {(decimal)Microsoft.VisualBasic.Financial.Pmt(percent, (double)quantityInstallments, -(double)(deferredAmount + intrestFirstInstallment), 0, Microsoft.VisualBasic.DueDate.BegOfPeriod) }");
                var intrestTableA = ((decimal)Microsoft.VisualBasic.Financial.Pmt(percent, (double)quantityInstallments, -(double)(deferredAmount + intrestFirstInstallment), 0, Microsoft.VisualBasic.DueDate.BegOfPeriod) * quantityInstallments) - (deferredAmount + intrestFirstInstallment);
                var totalIntrest = intrestFirstInstallment + intrestTableA;
                var daysInLoanMonth = DateTime.DaysInMonth(loanDate.Year, loanDate.Month);
                var totalServiceFees = CalculateServiceFees(serviceFeesPM, daysInFirstMonth, daysInLoanMonth, quantityMonthsAfterFirstMonth);
                //var totalServiceFees = 60;
                var serviceFeeVAT = totalServiceFees * vatRate / 100;
                var totalServiceFeesInclVAT = totalServiceFees + serviceFeeVAT;
                var subTotal = deferredAmount + totalIntrest + totalServiceFeesInclVAT;
                var creditLife = ((subTotal * (quantityInstallments + 1)) / 2) * ((decimal)creditProtectionRatePM / 100) * ((decimal)quantityMonthsRoundForInsurance / quantityInstallments);


                subTotal = isInsuranceRequired ? subTotal + creditLife : subTotal;
                var premium = isInsuranceRequired ? creditLife / quantityInstallments : 0;

                var intRatePerInstallment = (double)annualRateOfIntrest / frequency / 100;
                var tempVal1 = (double)Math.Pow((double)(1 + intRatePerInstallment), (double)-quantityInstallments);
                var tempVal2 = intRatePerInstallment / (1 - tempVal1);
                var tempVal3 = tempVal2 / (1 + intRatePerInstallment);

                decimal VAPAmt = 0;
                var totalVAP = 0.0M;
                bool IsVAPApplied = false;
                bool IsBeneficiary = false;
                if (isVAPChecked)
                {
                    try
                    {
                        log.Info("VAP calculations");
                        using (var result = new BackOfficeWebServiceClient("BackOfficeWebServer.NET"))
                        {
                            int CurrentappLoanPeriod = period * daysInOneTerm;
                            if (applicationId == 0)
                            {
                                IsVAPApplied = true;
                            }
                            else
                            {
                                IsVAPApplied = result.IsVAPApplied(applicationId, CurrentappLoanPeriod);
                                IsBeneficiary = IsVAPApplied;
                            }
                        }

                        int vapPeriodInMonths = period;

                        if (frequencyType == (int)SalaryFrequency.Fortnightly)
                        {
                            vapPeriodInMonths = (int)Math.Ceiling(Convert.ToDecimal(period) / Convert.ToDecimal(2));
                        }

                        if (frequencyType == (int)SalaryFrequency.Weekly)
                        {
                            vapPeriodInMonths = (int)Math.Ceiling(Convert.ToDecimal(period) / Convert.ToDecimal(4));
                        }

                        vapPeriodInMonths = vapPeriodInMonths > 12 ? 12 : vapPeriodInMonths;

                        VAPAmt = IsVAPApplied == true
                        ? CalculateVAPAmount(loanAmount, vapPeriodInMonths, (int)SalaryFrequency.Monthly, branchId, loanDate)
                         : 0;

                        if (VAPAmt != 0)
                        {
                            if (frequencyType == (int)SalaryFrequency.Monthly)
                            {
                                totalVAP = VAPAmt * quantityInstallments;
                            }
                            else if (frequencyType == (int)SalaryFrequency.Fortnightly)
                            {
                                VAPAmt = VAPAmt / 2;
                                totalVAP = VAPAmt * quantityInstallments;
                            }
                            else if (frequencyType == (int)SalaryFrequency.Weekly)
                            {
                                VAPAmt = VAPAmt / 4;
                                totalVAP = VAPAmt * quantityInstallments;
                            }
                        }
                        else
                        {
                            log.Info("[GetCalculatedObject] VAP amount is zero for Application Id : " + applicationId);
                        }
                    }
                    catch (Exception ex)
                    {
                        log.Error(string.Format("Error calculating VAP. Error: {0}", ex));
                        throw;
                    }
                }

                var installment = (decimal)tempVal3 * (deferredAmount + intrest);

                var TotalInstallment = Math.Round(((totalServiceFeesInclVAT / period) + premium + installment + VAPAmt), 2);

                var discretionAmount = discretionAmt - TotalInstallment;

                var surplus = Math.Round(discretionAmt - (TotalInstallment * quantityFrequencyInAMonth), 2);
                if (frequencyType == (int)SalaryFrequency.Weekly)
                {
                    if (period < 4)
                        surplus = Math.Round(discretionAmt - (TotalInstallment * period), 2);
                }
                if (frequencyType == (int)SalaryFrequency.Fortnightly)
                {
                    if (period < 2)
                        surplus = Math.Round(discretionAmt - (TotalInstallment * period), 2);
                }
                var VAPDetails = GetVAPDetails(VAPAmt, branchId, loanDate, frequencyType, period);
                decimal _vatAmount = 0;
                return new
                {
                    Term = period,
                    DeferredAmount = Math.Round(deferredAmount, 2),
                    Interest = Math.Round(intrest, 2),
                    InitiationFee = CalculateInitiationFee(loanAmount),
                    ServiceFee = totalServiceFeesInclVAT,
                    Premium = Math.Round(premium, 2),
                    Installment = Math.Round(installment, 2),
                    TotalInstallment = Math.Round(TotalInstallment, 2),
                    LoanAmount = loanAmount,
                    RepaymentAmount = Math.Round(TotalInstallment * period, 2),
                    RepaymentDate = loanEndDate,
                    Surplus = surplus,
                    RemaningBalance = (Math.Round(deferredAmount, 2) + Math.Round(intrest, 2) + totalServiceFeesInclVAT + Math.Round(premium, 2)) - TotalInstallment,
                    VAP = IsVAPApplied,
                    VAP_Amount = totalVAP,
                    AnnualRateOfInterest = annualRateOfIntrest,
                    QuantityInstallments = quantityInstallments,
                    TotalInterest = totalIntrest,
                    FirstRepayDate = firstRepayDate,
                    ServiceFeeAmount = totalServiceFees,
                    ServiceVATAmount = serviceFeeVAT,
                    VAPPerInstallmentAmount = VAPAmt,
                    InitiationFeeAmount = CalculateInitiationFeeAndVAT(loanAmount, ref _vatAmount),
                    InitiationVATAmount = _vatAmount,
                    IsBeneficiaryrequried = IsBeneficiary,
                    BeneficiaryAmount = VAPDetails.BeneficiaryAmount,
                    Insured = VAPDetails.InsuredAmount,
                    LongTerm = daysInLoan > 60 ? true : false
                };
            }
            catch (Exception ex)
            {
                log.Error(string.Format("[GetCalculatedObject] Error calculating loan terms for application {0}, loan amount {1}, discretion {2}, period {3}, VAP {4}, Insurance {5}, interest rate {6}, pay day {7}\nError: {8}", applicationId, loanAmount, discretionAmt, period, isVAPChecked, isInsuranceRequired, annualRateOfIntrest, payDay, ex));
                throw;
            }
        }

        public DateTime? GetFirstRepaymentDateByRules(DateTime loanDate, int frequencyType, int payDay)
        {
            try
            {
                log.Info("[GetFirstRepaymentDate] function started.");
                int daysInFirstInstallment = 0;
                var daysInOneTerm = 30;
                var daysPerMonth = 30;
                var monthsInYear = 12;
                int frequency = 12;
                decimal serviceFeesPM = 60;
                decimal vatRate = 15;
                decimal quantityFrequencyInAMonth = 1;
                //var firstRepayDate = loanDate;                
                var firstRepayDate = loanDate.AddDays(3);

                //var RepaymentDate = loanDate;
                var RepaymentDate = loanDate.AddDays(3);
                bool isAdditionalDaysInFirstInstallment = false;

                if (frequencyType == (int)SalaryFrequency.Monthly)
                {
                    frequency = 12;
                    daysInOneTerm = 30;
                    quantityFrequencyInAMonth = 1;

                    DateTime paydayca = new DateTime(loanDate.Year, loanDate.Month, loanDate.Day).AddDays(15);
                    DateTime payDate = new DateTime();
                    try
                    {
                        payDate = new DateTime(loanDate.Year, loanDate.Month, payDay);
                    }
                    catch (Exception ex)
                    {
                        payDate = new DateTime(loanDate.Year, loanDate.Month, DateTime.DaysInMonth(loanDate.Year, loanDate.Month));
                    }
                    if (payDate <= paydayca)
                    {
                        firstRepayDate = payDate.AddMonths(1);
                        if ((firstRepayDate - loanDate).Days < 15)
                        {
                            firstRepayDate = firstRepayDate.AddMonths(1);
                        }
                    }
                    else
                    {
                        firstRepayDate = payDate;
                    }
                }

                if (frequencyType == (int)SalaryFrequency.Fortnightly)
                {
                    frequency = 26;
                    daysInOneTerm = 14;
                    quantityFrequencyInAMonth = 2;

                    DateTime paydayca = new DateTime(loanDate.Year, loanDate.Month, loanDate.Day).AddDays(7);
                    if ((new DateTime(loanDate.Year, loanDate.Month, payDay)) <= paydayca)
                    {
                        firstRepayDate = new DateTime(loanDate.Year, loanDate.Month, payDay).AddDays(14);
                        if ((firstRepayDate - loanDate).Days < 7)
                        {
                            firstRepayDate = firstRepayDate.AddDays(14);
                        }
                        if ((firstRepayDate - loanDate).Days < 7)
                        {
                            firstRepayDate = firstRepayDate.AddDays(14);
                        }
                    }
                    else
                    {
                        firstRepayDate = new DateTime(loanDate.Year, loanDate.Month, payDay);
                    }
                }

                if (frequencyType == (int)SalaryFrequency.Weekly)
                {
                    log.Info(string.Format("Calculating first repment date for Weekly frequecy"));
                    int additionalDays = 0;
                    if ((int)firstRepayDate.DayOfWeek > payDay)
                        additionalDays = 7 - ((int)firstRepayDate.DayOfWeek - payDay);
                    else
                        additionalDays = payDay - (int)firstRepayDate.DayOfWeek;

                    log.Info("additionalDays: " + additionalDays);
                    frequency = 52;
                    daysInOneTerm = 7;
                    quantityFrequencyInAMonth = 4;
                    firstRepayDate = firstRepayDate.AddDays(additionalDays);
                    //RepaymentDate = RepaymentDate.AddDays(daysInOneTerm * (period - 1)).AddDays(additionalDays);
                    int workingDays = GetWorkingDays(loanDate, firstRepayDate);
                    log.Info("working days: " + workingDays);
                    if (workingDays < 3)
                    {
                        firstRepayDate = firstRepayDate.AddDays(7);
                        log.Info("First repay date after working days calc: " + firstRepayDate);
                        //RepaymentDate = RepaymentDate.AddDays(7);
                        //log.Info("repay date: " + RepaymentDate);
                        daysInFirstInstallment = (firstRepayDate - loanDate).Days + 1;
                        isAdditionalDaysInFirstInstallment = true;
                    }
                }
                log.Info("[GetFirstRepaymentDate] function end");
                return firstRepayDate;
            }
            catch (Exception ex)
            {
                log.Error("Error in [GetFirstRepaymentDate] function : " + ex);
                return null;
            }
        }

        private DateTime? GetLastRepaymentDateByRules(DateTime loanDate, DateTime firstRepayDate, int frequencyType, int payDay, int period)
        {
            try
            {
                log.Info("[GetLastRepaymentDateByRules] function started.");

                var RepaymentDate = loanDate.AddDays(3);
                if (frequencyType == (int)SalaryFrequency.Monthly)
                {
                    if (payDay > firstRepayDate.Day)
                    {
                        RepaymentDate = firstRepayDate.AddMonths((period - 1));
                        try
                        {
                            RepaymentDate = new DateTime(RepaymentDate.Year, RepaymentDate.Month, payDay);
                        }
                        catch (Exception ex)
                        {
                            RepaymentDate = new DateTime(RepaymentDate.Year, RepaymentDate.Month, DateTime.DaysInMonth(RepaymentDate.Year, RepaymentDate.Month));
                        }
                    }
                    else
                    {
                        RepaymentDate = firstRepayDate.AddMonths((period - 1));
                    }
                }

                if (frequencyType == (int)SalaryFrequency.Fortnightly)
                {
                    RepaymentDate = firstRepayDate.AddDays(14 * (period - 1));
                }

                if (frequencyType == (int)SalaryFrequency.Weekly)
                {

                    int additionalDays = 0;
                    if ((int)firstRepayDate.DayOfWeek > payDay)
                        additionalDays = 7 - ((int)firstRepayDate.DayOfWeek - payDay);
                    else
                        additionalDays = payDay - (int)firstRepayDate.DayOfWeek;

                    log.Info("additionalDays: " + additionalDays);

                    int daysInOneTerm = 7;


                    RepaymentDate = RepaymentDate.AddDays(daysInOneTerm * (period - 1)).AddDays(additionalDays);
                    int workingDays = GetWorkingDays(loanDate, firstRepayDate);
                    log.Info("working days: " + workingDays);
                    if (workingDays < 3)
                    {
                        RepaymentDate = RepaymentDate.AddDays(7);

                    }
                }

                return RepaymentDate;
            }
            catch (Exception ex)
            {
                log.Error("Error in [GetLastRepaymentDateByRules] function : " + ex);
                return null;
            }
        }

        private List<DateTime> GetCalculatedFirstRepaymentDate(DateTime loanDate, DateTime lastRepaymentDate, int frequencyType, int payDay)
        {
            try
            {
                log.Info("[GetCalculatedFirstRepaymentDate] function started.");
                DateTime validdate = loanDate.AddDays(3);
                DateTime enddate = lastRepaymentDate;
                List<DateTime> dates = new List<DateTime>();
                int num = 1;
                if (frequencyType == (int)SalaryFrequency.Monthly)
                {
                    while (enddate >= validdate && ValidatePayDay(payDay, enddate, frequencyType))
                    {
                        var prev = lastRepaymentDate.AddMonths(-(1 * num));
                        dates.Add(enddate);
                        enddate = prev;
                        num++;
                    }
                }
                if (frequencyType == (int)SalaryFrequency.Fortnightly)
                {
                    while (enddate >= validdate)
                    {
                        var prev = lastRepaymentDate.AddDays(-(14 * num));
                        dates.Add(enddate);
                        enddate = prev;
                        num++;
                    }
                }
                if (frequencyType == (int)SalaryFrequency.Weekly)
                {
                    while (enddate >= validdate)
                    {
                        var prev = lastRepaymentDate.AddDays(-(7 * num));
                        dates.Add(enddate);
                        enddate = prev;
                        num++;
                    }
                }
                log.Info("[GetCalculatedFirstRepaymentDate] function end.");
                return dates.AsEnumerable().Reverse().ToList();
            }
            catch (Exception ex)
            {
                log.Error("Error in [GetCalculatedFirstRepaymentDate] function." + ex);
                return null;
            }
        }

        private DateTime? GetCalculatedLastRepaymentDate(DateTime loanDate, DateTime firstRepaymentDate, int frequencyType, int payDay, int period)
        {
            try
            {
                log.Info("[GetCalculatedLastRepaymentDate] function started.");
                List<DateTime> dates = new List<DateTime>();
                int num = 1;
                DateTime endDate = firstRepaymentDate;
                if (frequencyType == (int)SalaryFrequency.Monthly)
                {
                    while (num < period && ValidatePayDay(payDay, endDate, frequencyType))
                    {
                        endDate = firstRepaymentDate.AddMonths(1 * num);
                        num++;
                    }

                }
                if (frequencyType == (int)SalaryFrequency.Fortnightly)
                {
                    while (num < period)
                    {
                        endDate = firstRepaymentDate.AddDays(14 * num);
                        num++;
                    }
                }
                if (frequencyType == (int)SalaryFrequency.Weekly)
                {
                    while (num < period)
                    {
                        endDate = firstRepaymentDate.AddDays(7 * num);
                        num++;
                    }
                }
                log.Info("[GetCalculatedLastRepaymentDate] function end.");
                return endDate;
            }
            catch (Exception ex)
            {
                log.Error("Error in [GetCalculatedLastRepaymentDate] function." + ex);
                return null;
            }
        }

        private bool ValidatePayDay(int payDay, DateTime dateToValidate, int frequencyType)
        {
            try
            {
                log.Info("[ValidatePayDay] function started");
                var isValidDay = false;
                if (frequencyType == (int)SalaryFrequency.Fortnightly || frequencyType == (int)SalaryFrequency.Weekly || frequencyType == (int)SalaryFrequency.Monthly)
                {
                    isValidDay = true;
                }
                //if (frequencyType == (int)SalaryFrequency.Monthly)
                //{
                //    if (dateToValidate.Day == payDay)
                //    {
                //        isValidDay =  true;
                //    }
                //    else
                //    {
                //        int[] validDays = { 28, 29, 30, 31 };
                //        switch (payDay)
                //        {
                //            case 31:
                //                validDays = new int[] { 28, 29, 30 };
                //                break;
                //            case 30:
                //                validDays = new int[] { 28, 29 };
                //                break;
                //            case 29:
                //                validDays = new int[] { 28 };
                //                break;
                //        }
                //        var dayCountInMonth = DateTime.DaysInMonth(dateToValidate.Year, dateToValidate.Month);
                //        //if (validDays.Contains(dayCountInMonth))
                //        //{
                //        //    return true;
                //        //}
                //        //else
                //        //{
                //        //    return false;
                //        //}
                //        foreach(var day in validDays)
                //        {
                //            if(dateToValidate.Day == day)
                //            {
                //                return true;
                //            }
                //        }
                //    }
                //}
                return isValidDay;
            }
            catch (Exception ex)
            {
                log.Info("Error in [ValidatePayDay] function : " + ex);
                return false;
            }
        }

        private int GetWorkingDays(DateTime from, DateTime to)
        {
            var dayDifference = (int)to.Subtract(from).TotalDays;
            return Enumerable
                .Range(1, dayDifference)
                .Select(x => from.AddDays(x))
                .Count(x => x.DayOfWeek != DayOfWeek.Saturday && x.DayOfWeek != DayOfWeek.Sunday);
        }

        [NonAction]
        private static decimal CalculateInitiationFee(decimal loanAmount)
        {
            try
            {
                log.Info(string.Format("Calculate initiation fee for loan amount {0}", loanAmount));
                decimal[] initiationFee = new decimal[3];

                initiationFee[0] = loanAmount > 1000 ? (decimal)(1000 * (16.5 / 100)) + ((loanAmount - 1000) * 10 / 100) : (decimal)(1000 * (16.5 / 100));
                initiationFee[1] = 1050;
                initiationFee[2] = loanAmount * 15 / 100;
                return initiationFee.Min() + (initiationFee.Min() * 15 / 100);
            }
            catch (Exception ex)
            {
                log.Error(string.Format("Error calculating initiation fee for loan amount {0}\nError: {1}", loanAmount, ex));
                throw;
            }
        }


        //[NonAction]
        //private decimal CalculateServiceFees(DateTime startDate, DateTime endDate, decimal quantityInstallments = 1, int annualRateOfIntrest = 60)
        //{
        //    try
        //    {
        //        log.Info(string.Format("Calculate Service fee for start date {0}, end date {1}, instalments {2}, interest {3}", startDate, endDate, quantityInstallments, annualRateOfIntrest));
        //        int startDay = startDate.Day;
        //        int endDay = endDate.Day;
        //        int months = endDate.Month - startDate.Month;
        //        if (endDate.Month <= startDate.Month && endDate.Year > startDate.Year)
        //            months = 12 + months;
        //        int initialMonthDays = DateTime.DaysInMonth(startDate.Year, startDate.Month) - startDay + 1;
        //        decimal r60 = annualRateOfIntrest * initialMonthDays / 30;

        //        for (int i = 0; i < months; i++)
        //            r60 += annualRateOfIntrest;

        //        r60 += r60 * 15 / 100;
        //        return Math.Round(r60 / quantityInstallments, 2);
        //    }
        //    catch (Exception ex)
        //    {
        //        log.Error(string.Format("Calculate Service fee for start date {0}, end date {1}, instalments {2}, interest {3}\nError: {4}", startDate, endDate, quantityInstallments, annualRateOfIntrest, ex));
        //        throw;
        //    }
        //}

        [NonAction]
        public decimal CalculateServiceFees(decimal serviceFeePM, int daysInFirstMonth, int daysPerMonth, int quantityMonthsAfterFirstMonth)
        {
            // service fee PM * days in first month / ProRetaDays  
            //QuantityMonths * service fee PM

            decimal firstMonthServiceFees = serviceFeePM * daysInFirstMonth / daysPerMonth;
            decimal serviceFees = quantityMonthsAfterFirstMonth * serviceFeePM;
            return Math.Round(firstMonthServiceFees + serviceFees, 2);
        }


        public Response<int> GenerateOTP(string obj, int objId, string type, int typeId)
        {
            try
            {
                log.Info(string.Format("Generate OTP for obj {0}, objId {1}, type {2}, typeid {3}", obj, objId, type, typeId));
                var sendOTP = 0;
                using (var result = new BackOfficeWebServiceClient("BackOfficeWebServer.NET"))
                {
                    var filterConditions = FilterUtil.GetFiltersAsString(BO_Object.Applications, Convert.ToInt32(HttpContext.Current.Session["UserId"]));
                    var application = result.GetApplicationDetail(filterConditions, objId);
                    if (application != null)
                        if (application.ApplicationClient.CellNo == null)
                            return Response<int>.CreateResponse(Constants.Failed, 0, new ErrorHandler() { ErrorCode = Constants.ApplicationErrorCode, Message = Constants.ContactDetailsErrorMessage });
                        else
                            sendOTP = result.OTP_Send(obj, objId, type, typeId, application.ApplicationClient.CellNo);
                    if (sendOTP == 0)
                        return Response<int>.CreateResponse(Constants.Failed, 0, new ErrorHandler() { ErrorCode = Constants.ApplicationErrorCode, Message = Constants.GenerateOTPErrorMessage });
                }
                return Response<int>.CreateResponse(Constants.Success, sendOTP, null);
            }
            catch (Exception ex)
            {
                log.Error(string.Format("Error generating OTP for obj {0}, objId {1}, type {2}, typeid {3}\nError: {4}", obj, objId, type, typeId, ex));
                throw;
            }
        }

        public Response<bool> VerifyOTP(int objId, int typeId, int OTP)
        {
            try
            {
                log.Info(string.Format("Verify OTP for objId {0}, typeid {1}, OTP {2}", objId, typeId, OTP));
                using (var result = new BackOfficeWebServiceClient("BackOfficeWebServer.NET"))
                {
                    var verifyOTP = result.OTP_Verify(objId, typeId, OTP);
                    if (!verifyOTP)
                    {
                        log.Info(string.Format("Verify OTP failed for objId {0}, typeid {1}, OTP {2}", objId, typeId, OTP));
                        return Response<bool>.CreateResponse(Constants.Failed, false, new ErrorHandler() { ErrorCode = Constants.ApplicationErrorCode, Message = Constants.InvalidOTP });
                    }
                    else
                    {
                        log.Info(string.Format("Verify OTP success for objId {0}, typeid {1}, OTP {2}", objId, typeId, OTP));
                        long personId = 0;
                        try
                        {
                            var filterConditions = FilterUtil.GetFiltersAsString(BO_Object.Applications, Convert.ToInt32(HttpContext.Current.Session["UserId"]));
                            var application = result.GetApplicationDetail(filterConditions, objId);

                            if (application != null)
                            {
                                log.Info(string.Format("Application {0} details found", objId));
                                PER_Person person = new PER_Person();
                                using (var uow = new UnitOfWork())
                                {
                                    try
                                    {
                                        log.Info(string.Format("Get person details found for Idnumber {0}.", application.ApplicationClient.IDNumber));
                                        person = new XPQuery<PER_Person>(uow).FirstOrDefault(p => p.IdNum == application.ApplicationClient.IDNumber);
                                    }
                                    catch (Exception ex)
                                    {
                                        log.Error(string.Format("Error getting person details for Id number {0} when verifying OTP {1} for obj {2} and type {2}\nError: {3}", application.ApplicationClient.IDNumber, OTP, objId, typeId, ex));
                                        throw;
                                    }
                                }
                                if (person == null)
                                {
                                    log.Info(string.Format("Person details not found for Idnumber {0}. Add as new person.", application.ApplicationClient.IDNumber));
                                    try
                                    {
                                        var addPerson = AddPerson(objId);
                                        if (addPerson.Status == Constants.Success)
                                            personId = addPerson.Data.personid;
                                        else
                                            return Response<bool>.CreateResponse(Constants.Failed, false, new ErrorHandler() { ErrorCode = Constants.ApplicationErrorCode, Message = addPerson.Error.Message });
                                    }
                                    catch (Exception ex)
                                    {
                                        log.Error(string.Format("Error adding person for Id number {0} when verifying OTP {1} for obj {2} and type {2}\nError: {3}", application.ApplicationClient.IDNumber, OTP, objId, typeId, ex));
                                        throw;
                                    }
                                }
                                try
                                {
                                    log.Info(string.Format("disbursment for obj {0} and person {1}", objId, personId));
                                    var staffPersonId = HttpContext.Current.Session["PersonId"];
                                    if (staffPersonId == null)
                                    {
                                        log.Fatal(String.Format($"method: VerifyOTP(int objId, int typeId, int OTP), Failed. Staff Personid not found in session variable"));
                                        return Response<bool>.CreateResponse(Constants.Failed, false, new ErrorHandler() { ErrorCode = Constants.ApplicationErrorCode, Message = Constants.ConsultantPersonIdNotFound });
                                    }
                                    var res = result.Disbursement(objId, personId, Convert.ToInt64(staffPersonId));
                                }
                                catch (Exception ex)
                                {
                                    log.Error(string.Format("Error in disbursment obj {0} and person {1}\nError: {2}", objId, personId, ex));
                                    throw;
                                }

                                try
                                {
                                    log.Info(string.Format("[UpdateQuoteAcceptance] for typeId {0} and OTP {1}", typeId, OTP));
                                    result.UpdateQuoteAcceptance(typeId, OTP);
                                }
                                catch (Exception ex)
                                {
                                    log.Error(string.Format("Error in [UpdateQuoteAcceptance] for typeId {0} and OTP {1}\nError: {2}", typeId, OTP, ex));
                                    throw;
                                }
                                try
                                {
                                    log.Info(string.Format("[UpdateOTPStatus] for objId {0}, typeId {1} and OTP {2}", objId, typeId, OTP));
                                    result.UpdateOTPStatus(objId, typeId, OTP);
                                }
                                catch (Exception ex)
                                {
                                    log.Error(string.Format("Error in [UpdateOTPStatus] for objId {0}, typeId {1} and OTP {2}\nError: {2}", objId, typeId, OTP, ex));
                                    throw;
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            log.Error(string.Format("Error getting application {0} details when verifying OTP {1} for type {2}\nError: {3}", objId, OTP, typeId, ex));
                            throw;
                        }
                    }
                }
                log.Info(string.Format("Verify OTP scuccess for objId {0}, typeid {1}, OTP {2}", objId, typeId, OTP));
                return Response<bool>.CreateResponse(Constants.Success, true, null);
            }
            catch (Exception ex)
            {
                log.Error(string.Format("Error in verify OTP for objId {0}, typeid {1}, OTP {2}\nError: {3}", objId, typeId, OTP, ex));
                throw;
            }
        }

        [NonAction]
        private decimal CalculateVAPAmount(decimal loanAmt, int loanTerm, int salaryType, long? branchID, DateTime loanDate)
        {
            try
            {
                log.Info(string.Format("Calculate VAP amount for loan amount {0}, term {1}, salary type {2}", loanAmt, loanTerm, salaryType));
                //var myTerm = 0.0M;
                decimal vapAmt = 0;
                using (var result = new BackOfficeWebServiceClient("BackOfficeWebServer.NET"))
                {
                    long branchId = branchID != null ? branchID.Value : 0;
                    List<VAP_ChargesDto> vapCharges = result.GetVAPCharges(branchId).ToList();
                    if (vapCharges != null && vapCharges.Count() > 0)
                    {
                        int bandId = result.GetVAPBandId(loanAmt);

                        int vapId = result.GetVAPIdVAPBrandBranch(bandId, branchId, loanDate);

                        //myTerm = Convert.ToDecimal(loanTerm);

                        //switch (frequencyType)
                        //{
                        //    case (int)SalaryFrequency.Monthly:
                        //        myTerm = myTerm / Convert.ToDecimal(1);

                        //        break;
                        //    case (int)SalaryFrequency.Fortnightly:
                        //        myTerm = myTerm / Convert.ToDecimal(2);

                        //        break;
                        //    case (int)SalaryFrequency.Weekly:
                        //        myTerm = myTerm / Convert.ToDecimal(4);

                        //        break;
                        //}

                        //myTerm = Math.Ceiling(myTerm);
                        //myTerm = myTerm > 12 ? 12 : myTerm;

                        if (vapId <= 0) { return vapAmt; }
                        VAP_ChargesDto validRow = vapCharges.
                        Where(c => c.Vap.VapId == vapId && c.Term == loanTerm && c.Vap.BranchId == branchId).FirstOrDefault();
                        vapAmt = validRow != null ? validRow.VapAmount : 0;
                    }
                }

                return vapAmt;
            }
            catch (Exception ex)
            {
                log.Error(string.Format("Error calculating VAP amount for loan amount {0}, term {1}, salary type {2}\nError: {3}", loanAmt, loanTerm, salaryType, ex));
                throw;
            }
        }

        [HttpGet]
        [Route("applications/GetPdf/{id}")]
        public HttpResponseMessage GetMultiplePdf(int id)
        {
            try
            {
                log.Info(string.Format("Get PDF for application {0}", id));
                bool otp = true;

                List<string> _docCopy = new List<string>();
                _docCopy.Add("Client");
                _docCopy.Add("Atlas");

                // Generating the Customer Acceptance Pdf
                for (int i = 0; i < _docCopy.Count(); i++)
                {
                    log.Info(String.Format($"[Method:GetMultiplePdf]Generating Quote pdf for application {id}"));
                    GetAgreePdf(id, otp, "QUOTE.snx", _docCopy[i]);

                    log.Info(String.Format($"[Method:GetMultiplePdf]Generating mag_quot pdf for application {id}"));
                    GetContractPdf(id, "mag_quot", false, _docCopy[i]);

                    log.Info(String.Format($"[Method:GetMultiplePdf]Generating INSURE pdf for application {id}"));
                    GetContractPdf(id, "INSURE", false, _docCopy[i]);

                    log.Info(String.Format($"[Method:GetMultiplePdf]Generating Quote pdf for application {id}"));
                    GetAgreePdf(id, false, "AGREE.snx", _docCopy[i]);

                    string[] path = new string[3];

                    path[0] = getFilePath("Quotations", "_Quota", id, _docCopy[i]); ;
                    //path[1] = getFilePath("VAP_Contract", "mag_quot_", id, _docCopy[i]); ;
                    path[1] = getFilePath("CreditLife", "INSURE_", id, _docCopy[i]);
                    path[2] = getFilePath("Quotations", "_Agree", id, _docCopy[i]);

                    MergeMultiplePDFIntoSinglePDF(getFilePath("CustomerAcceptanceContracts", "_Quota", id, _docCopy[i]), path);

                    otp = false;

                }




                //Get all files from the given folder path.



                //Merge Multiple Pdf.



                //Reading file as a stream.
                var stream = File.OpenRead(getFilePath("CustomerAcceptanceContracts", "_Quota", id, _docCopy[0]));


                // Converting Pdf files to the byte array.
                var result = FileToHttpResponseMessage(stream, "file.pdf", "application/pdf", id);

                // Returning file as a Byte Array
                return result;
            }
            catch (Exception ex)
            {
                log.Error(string.Format("[Method:GetMultiplePdf] Error in Get PDF for application {0}\nError: {1}", id, ex));
                throw;
            }

        }

        private void MergeMultiplePDFIntoSinglePDF(string outputFilePath, string[] pdfFiles)
        {
            try
            {
                PdfDocument document = new PdfDocument();

                //To Delete the File
                //string[] paths = Directory.GetFiles(HttpContext.Current.Server.MapPath("~/Content/CustomerAcceptanceContracts/"));
                //foreach (var path in paths)
                //{
                //    System.IO.File.Delete(path);
                //}

                foreach (string pdfFile in pdfFiles)
                {
                    PdfDocument inputPDFDocument = PdfReader.Open(pdfFile, PdfDocumentOpenMode.Import);
                    document.Version = inputPDFDocument.Version;
                    foreach (PdfPage page in inputPDFDocument.Pages)
                    {
                        document.AddPage(page);
                    }
                    // When document is add in pdf document remove file from folder  

                }
                // Set font for paging  
                XFont font = new XFont("Segoe UI", 8);
                XBrush brush = XBrushes.Black;
                // Create variable that store page count  
                string noPages = document.Pages.Count.ToString();
                // Set for loop of document page count and set page number using DrawString function of PdfSharp  
                for (int i = 0; i < document.Pages.Count; ++i)
                {
                    PdfPage page = document.Pages[i];
                    // Make a layout rectangle.  
                    XRect layoutRectangle = new XRect(240 /*X*/ , page.Height - font.Height - 10 /*Y*/ , page.Width /*Width*/ , font.Height /*Height*/ );
                    using (XGraphics gfx = XGraphics.FromPdfPage(page))
                    {
                        gfx.DrawString("Page " + (i + 1).ToString() + " of " + noPages, font, brush, layoutRectangle, XStringFormats.Center);
                    }
                }
                document.Options.CompressContentStreams = true;
                document.Options.NoCompression = false;
                // In the final stage, all documents are merged and save in your output file path.  
                document.Save(outputFilePath);
            }
            catch (Exception ex)
            {
                log.Error(String.Format("[MergeMultiplePDFIntoSinglePDF] Error in Merging pdf Files"));
                throw ex;
            }

        }

        public void InsertShedulePayment(int applicationId, QuotationDetails quotation, List<VMLoanAccount> loantype, int frequencyTypeId)
        {
            try
            {
                //log.Info(string.Format("insert payment schedule for loanAmount {0}, discretionAmt {1}, loanDate {2}, frequency type {3}, loanTerms {4}, maturityDate {5}, applicationId {6}, VAP {7}, accountId {8}, Insurance {9}", loanAmount, discretionAmt, loanDate, frequencyTypeId, loanTerms, maturityDate, applicationId, isVAP, loantype, isInsuranceRequired));
                List<dynamic> lst = new List<dynamic>();
                var startdate = quotation.LoanDate.Value;
                var endDate = startdate;
                //int annualRateOfIntrest = DBManager.GetInterestRate(applicationId, quotation.LoanDate.Value);
                endDate = Convert.ToDateTime(quotation.Enddate);
                int payDay = 0;
                long? branchId = 0;
                using (var result = new BackOfficeWebServiceClient("BackOfficeWebServer.NET"))
                {
                    var filterConditions = FilterUtil.GetFiltersAsString(BO_Object.Applications, Convert.ToInt32(HttpContext.Current.Session["UserId"]));
                    ApplicationDto applicationDetails = result.GetApplicationDetail(filterConditions, applicationId);
                    payDay = applicationDetails != null ?
                        applicationDetails.Employer != null ? applicationDetails.Employer.PayDay : 0 : 0;
                    branchId = applicationDetails.ApplicationClient.Branch.BranchId;
                }
                //var obj = GetCalculatedObject(loanAmount, startdate, endDate, discretionAmt, loanTerms, frequencyTypeId, applicationId, isVAP, isInsuranceRequired, annualRateOfIntrest, payDay, branchId);

                //var ls = GetInstallment(obj, frequencyTypeId, startdate, endDate, loantype, quotation.Loanterm, payDay, branchId);
                var ls = GetInstallment(quotation, frequencyTypeId, startdate, endDate, loantype, quotation.Loanterm, payDay, branchId);
            }
            catch (Exception ex)
            {
                //log.Error(string.Format("error in insert payment schedule for loanAmount {0}, discretionAmt {1}, loanDate {2}, frequency type {3}, loanTerms {4}, maturityDate {5}, applicationId {6}, VAP {7}, accountId {8}, Insurance {9}\nError: {10}", loanAmount, discretionAmt, loanDate, frequencyTypeId, loanTerms, maturityDate, applicationId, isVAP,loantype, isInsuranceRequired, ex));
                throw ex;
            }
        }

        [NonAction]
        private dynamic GetInstallment(QuotationDetails quotation, int frequencyType, DateTime startdate, DateTime endDate, List<VMLoanAccount> accounts, int LoanTerms, int payDay, long? branchId)
        {
            try
            {
                log.Info(string.Format("Get installment for frequencyType {0}, startdate {1}, endDate {2}, AccountId {3}, LoanTerms {4}, payDay {5}", frequencyType, startdate, endDate, accounts, LoanTerms, payDay));

                var daysInOneTerm = 30;
                var frequency = 12;
                decimal quantityFrequencyInAMonth = 1;
                var RepaymentDate = startdate;
                var daysInFirstInstallment = 0;
                var daysPerMonth = 30;
                var monthsInYear = 12;
                var firstRepayDate = startdate.AddDays(3);
                if (frequencyType == (int)SalaryFrequency.Monthly)
                {
                    frequency = 12;
                    daysInOneTerm = 30;
                    quantityFrequencyInAMonth = 1;
                }
                if (frequencyType == (int)SalaryFrequency.Fortnightly)
                {
                    frequency = 26;
                    daysInOneTerm = 14;
                    quantityFrequencyInAMonth = 2;
                }
                if (frequencyType == (int)SalaryFrequency.Weekly)
                {
                    frequency = 52;
                    daysInOneTerm = 7;
                    quantityFrequencyInAMonth = 4;
                }

                var annualRateOfIntrest = quotation.AnnualRateOfInterest;// loanlist.annualRateOfIntrest;
                var daysInLoan = (int)Math.Floor((endDate - startdate).TotalDays + 1);
                var quantityInstallments = LoanTerms;
                List<dynamic> lst = new List<dynamic>();

                var VAPDetails = GetVAPDetails(quotation.VAPPerInstallmentAmount, branchId, quotation.FirstRepayDate.Value, frequencyType, LoanTerms);
                for (int k = 0; k < accounts.Count(); k++)
                {
                    if (accounts[k].LoanType.ToUpper() == "NUCARD")
                        continue;

                    lst = new List<dynamic>();

                    for (int i = 0; i < quotation.Loanterm; i++)
                    {
                        RepaymentDate = startdate.AddDays(3);
                        if (frequencyType == (int)SalaryFrequency.Monthly)
                        {
                            log.Info(string.Format("[GetInstallment] Monthly calculation for term {0}", i));
                            try
                            {
                                firstRepayDate = quotation.FirstRepayDate.Value; // loanlist.FirstRepayDate;
                                RepaymentDate = quotation.FirstRepayDate.Value; //loanlist.FirstRepayDate;
                                if (i > 0)
                                {
                                    RepaymentDate = RepaymentDate.AddMonths(i);
                                }
                            }
                            catch (Exception ex)
                            {
                                log.Error(string.Format("Error in [GetInstallment] Monthly calculation for term {0}\nError: {1}", i, ex));
                                throw;
                            }
                        }
                        if (frequencyType == (int)SalaryFrequency.Fortnightly)
                        {
                            log.Info(string.Format("[GetInstallment] Fortnightly calculation for term {0}", i));
                            try
                            {
                                daysInOneTerm = 14;
                                firstRepayDate = quotation.FirstRepayDate.Value; //loanlist.FirstRepayDate;
                                RepaymentDate = quotation.FirstRepayDate.Value; //loanlist.FirstRepayDate;
                                if (i > 0)
                                {
                                    RepaymentDate = RepaymentDate.AddDays(daysInOneTerm * i);
                                }
                            }
                            catch (Exception ex)
                            {
                                log.Error(string.Format("Error in [GetInstallment] Fortnightly calculation for term {0}\nError: {1}", i, ex));
                                throw;
                            }
                        }
                        if (frequencyType == (int)SalaryFrequency.Weekly)
                        {
                            try
                            {
                                log.Info(string.Format("Calculating firstRepayDate and RepaymentDate for Weekly frequecy"));
                                frequency = 52;
                                daysInOneTerm = 7;
                                quantityFrequencyInAMonth = 4;
                                firstRepayDate = quotation.FirstRepayDate.Value;
                                RepaymentDate = quotation.FirstRepayDate.Value;
                                if (i > 0)
                                {
                                    RepaymentDate = RepaymentDate.AddDays(daysInOneTerm * i);
                                }
                                log.Info("Last Repay date after working days calc: " + RepaymentDate);
                            }
                            catch (Exception ex)
                            {
                                log.Error(string.Format("Error calculating firstRepayDate and RepaymentDate for Weekly frequecy\nError: {0}", ex));
                                throw;
                            }
                        }
                        if (accounts[k].LoanType.ToUpper() == "LOAN")
                        {
                            if (i == 0)
                            {

                                try
                                {
                                    log.Info(string.Format("Calculate Loan first installment"));
                                    var qtyMonthsPerMonthlyTerm = (double)(quantityInstallments / quantityFrequencyInAMonth);
                                    var quantityMonthsRoundForInsurance = Math.Round(qtyMonthsPerMonthlyTerm, 0, MidpointRounding.AwayFromZero);
                                    daysInFirstInstallment = daysInFirstInstallment > 0 ? daysInFirstInstallment : Convert.ToInt32(daysInLoan - daysInOneTerm * (quantityInstallments - 1));
                                    var intrest = (quotation.DeferredAmount * annualRateOfIntrest / monthsInYear) / 100 * daysInFirstInstallment / daysPerMonth;

                                    lst.Add(new
                                    {
                                        Term = i + 1,
                                        DeferredAmount = quotation.DeferredAmount,
                                        Interest = intrest,
                                        ServiceFee = (quotation.ServiceFee / LoanTerms),
                                        Premium = quotation.Premium,
                                        Installment = quotation.Installment,
                                        TotalInstallment = quotation.TotalInstallment,
                                        RemaningBalance = ((quotation.DeferredAmount + intrest + quotation.Premium + (quotation.ServiceFee / LoanTerms) + quotation.VAPPerInstallmentAmount) - quotation.TotalInstallment),
                                        RepaymentDate = firstRepayDate,
                                        NoOfdays = daysInFirstInstallment,
                                        VAP_Amount = Math.Round(quotation.VAPPerInstallmentAmount, 2),
                                        InitiationFee = quotation.InitiationFee,
                                        VAP_Funeral = VAPDetails.FuneralPerInstallment,
                                        VAP_TAX = VAPDetails.TAXPerInstallment,
                                        VAP_Other = VAPDetails.OthersPerInstallment
                                    });
                                }
                                catch (Exception ex)
                                {
                                    log.Error(string.Format("Error in [GetInstallment]. First installemnt calculation.\nError: {0}", ex));
                                    throw;
                                }
                            }
                            else
                            {
                                try
                                {
                                    log.Info(string.Format("Calculate next Loan installment"));
                                    var j = (i - 1);

                                    var Interst = lst[j].RemaningBalance * annualRateOfIntrest / frequency / 100;
                                    lst.Add(new
                                    {
                                        Term = i + 1,
                                        DeferredAmount = lst[j].RemaningBalance,
                                        Interest = Math.Round(Interst, 2),
                                        ServiceFee = (quotation.ServiceFee / LoanTerms),
                                        Premium = quotation.Premium,
                                        Installment = quotation.Installment,
                                        TotalInstallment = quotation.TotalInstallment,
                                        RemaningBalance = ((lst[j].RemaningBalance + Interst + quotation.Premium + (quotation.ServiceFee / LoanTerms) + quotation.VAPPerInstallmentAmount) - quotation.TotalInstallment),
                                        RepaymentDate = RepaymentDate,
                                        NoOfdays = daysInOneTerm,
                                        VAP_Amount = Math.Round(quotation.VAPPerInstallmentAmount, 2),
                                        InitiationFee = quotation.InitiationFee,
                                        VAP_Funeral = VAPDetails.FuneralPerInstallment,
                                        VAP_TAX = VAPDetails.TAXPerInstallment,
                                        VAP_Other = VAPDetails.OthersPerInstallment

                                    });
                                }
                                catch (Exception ex)
                                {
                                    log.Error(string.Format("Error in [GetInstallment]. Next installemnt calculation.\nError: {0}", ex));
                                    throw;
                                }
                            }
                        }
                        else if (accounts[k].LoanType.ToUpper() == "VAP")
                        {
                            if (i == 0)
                            {

                                try
                                {
                                    log.Info(string.Format("Calculate Vap first installment"));

                                    lst.Add(new
                                    {
                                        Term = i + 1,
                                        DeferredAmount = quotation.VAPAmount,
                                        Interest = 0,
                                        ServiceFee = 0,
                                        Premium = 0,
                                        Installment = VAPDetails.OthersPerInstallment,
                                        TotalInstallment = quotation.VAPPerInstallmentAmount,
                                        RemaningBalance = ((quotation.VAPAmount) - quotation.VAPPerInstallmentAmount),
                                        RepaymentDate = firstRepayDate,
                                        NoOfdays = daysInFirstInstallment,
                                        VAP_Amount = Math.Round(quotation.VAPPerInstallmentAmount, 2),
                                        InitiationFee = 0,
                                        VAP_Funeral = VAPDetails.FuneralPerInstallment,
                                        VAP_TAX = VAPDetails.TAXPerInstallment,
                                        VAP_Other = VAPDetails.OthersPerInstallment
                                    });
                                }
                                catch (Exception ex)
                                {
                                    log.Error(string.Format("Error in [GetInstallment]. First Vap installemnt calculation.\nError: {0}", ex));
                                    throw;
                                }
                            }
                            else
                            {
                                try
                                {
                                    log.Info(string.Format("Calculate next Vap installment"));
                                    var j = (i - 1);

                                    lst.Add(new
                                    {
                                        Term = i + 1,
                                        DeferredAmount = lst[j].RemaningBalance,
                                        Interest = 0,
                                        ServiceFee = 0,
                                        Premium = 0,
                                        Installment = VAPDetails.OthersPerInstallment,
                                        TotalInstallment = quotation.VAPPerInstallmentAmount,
                                        RemaningBalance = (lst[j].RemaningBalance - quotation.VAPPerInstallmentAmount),
                                        RepaymentDate = RepaymentDate,
                                        NoOfdays = daysInOneTerm,
                                        VAP_Amount = Math.Round(quotation.VAPPerInstallmentAmount, 2),
                                        InitiationFee = 0,
                                        VAP_Funeral = VAPDetails.FuneralPerInstallment,
                                        VAP_TAX = VAPDetails.TAXPerInstallment,
                                        VAP_Other = VAPDetails.OthersPerInstallment

                                    });
                                }
                                catch (Exception ex)
                                {
                                    log.Error(string.Format("Error in [GetInstallment]. Next Vap installemnt calculation.\nError: {0}", ex));
                                    throw;
                                }
                            }
                        }
                    }
                    try
                    {
                        using (var uow = new UnitOfWork())
                        {
                            var schedules = new XPQuery<ACC_Schedules>(uow).Where(schedule => schedule.AccountId == accounts[k].AccountId).ToList();
                            if (schedules.Count <= LoanTerms)
                            {
                                DBManager.SaveInstallment(lst, accounts[k].AccountId);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        log.Error(string.Format("Error saving installment. Account {0}.\nError: {1}", accounts[k].AccountId, ex));
                        throw;
                    }
                    using (var uow = new UnitOfWork())
                    {
                        try
                        {
                            var account = new XPQuery<ACC_Account>(uow).Where(x => x.AccountId == accounts[k].AccountId).FirstOrDefault();
                            if (accounts[k].LoanType.ToUpper() == "LOAN")
                            {
                                try
                                {
                                    var sum = lst.Select(x => x.Interest + x.Premium + x.ServiceFee + x.VAP_Amount).Cast<decimal?>().Sum() + quotation.InitiationFee;
                                    account.LoanAmount = quotation.LoanAmount;
                                    account.Period = daysInLoan;
                                    account.PeriodFrequency = new XPQuery<ACC_PeriodFrequency>(uow).FirstOrDefault(p => p.PeriodFrequencyId == frequencyType);
                                    account.AccountBalance = quotation.RepaymentAmount;
                                    account.InstalmentAmount = quotation.TotalInstallment;
                                    account.TotalFees = Convert.ToDecimal(sum);
                                    account.InterestRate = (float)quotation.AnnualRateOfInterest;
                                    account.NumOfInstalments = LoanTerms;
                                    account.FirstInstalmentDate = quotation.FirstRepayDate;
                                    uow.Save(account);
                                    uow.CommitChanges();
                                }
                                catch (Exception ex)
                                {
                                    log.Error(string.Format("Error in calculation for account {0} details.\nError: {1}", accounts[k].AccountId, ex));
                                    throw;
                                }
                            }
                            else if (accounts[k].LoanType.ToUpper() == "VAP")
                            {
                                try
                                {
                                    var sum = lst.Select(x => x.VAP_Amount).Cast<decimal?>().Sum();
                                    account.LoanAmount = quotation.VAPAmount;
                                    account.Period = daysInLoan;
                                    account.PeriodFrequency = new XPQuery<ACC_PeriodFrequency>(uow).FirstOrDefault(p => p.PeriodFrequencyId == frequencyType);
                                    account.AccountBalance = quotation.VAPAmount;
                                    account.InstalmentAmount = quotation.VAPPerInstallmentAmount;
                                    account.TotalFees = Convert.ToDecimal(sum);
                                    account.InterestRate = (float)quotation.AnnualRateOfInterest;
                                    account.NumOfInstalments = LoanTerms;
                                    account.FirstInstalmentDate = quotation.FirstRepayDate.Value;
                                    uow.Save(account);
                                    uow.CommitChanges();
                                }
                                catch (Exception ex)
                                {
                                    log.Error(string.Format("Error in calculation for account {0} details.\nError: {1}", accounts[k].AccountId, ex));
                                    throw;
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            log.Error(string.Format("Error getting account {0} details.\nError: {1}", accounts.ToString(), ex));
                            throw;
                        }
                    }
                }
                log.Info(string.Format("Number of installments: {0}", lst.Count()));
                return lst;
            }
            catch (Exception ex)
            {
                log.Error(string.Format("Error getting installment for frequencyType {0}, startdate {1}, endDate {2}, AccountId {3}, LoanTerms {4}, payDay {5}\nError: {6}", frequencyType, startdate, endDate, accounts, LoanTerms, payDay, ex));
                throw;
            }
        }



        [NonAction]
        public dynamic GetVAPDetails(decimal VapAmount, long? branchId, DateTime loanDate, int frequencyType, int loanTerm)
        {
            dynamic returnValue = new
            {
                TotalTAX = 0,
                TotalFuneralSP = 0,
                TotalOthers = 0,
                TAXPerInstallment = 0,
                FuneralPerInstallment = 0,
                OthersPerInstallment = 0,
                VAP_CST = 0,
                MaxAge = 0,
                Age_14_21 = 0,
                Age_06_13 = 0,
                Age_01_05 = 0,
                Age_00_11mt = 0,
                VapAmountperInstallment = 0,
                BeneficiaryAmount = 0,
                InsuredAmount = 0,

            };

            using (var result = new BackOfficeWebServiceClient("BackOfficeWebServer.NET"))
            {
                var totalVap = VapAmount * loanTerm;

                if (totalVap <= 0) { return returnValue; }

                int bandID = result.GetVAPDESCBandId(totalVap);
                int vapId = result.GetVAPIdVAPDESCBrandBranch(bandID, (branchId != null ? branchId.Value : 0), loanDate);
                var vapDetailsList = result.GetVAPDESCChargeList();

                var vapDetailsRow = vapDetailsList.Where(c => c.Desc_Vap.Desc_VapId == vapId).FirstOrDefault();
                var loanTermDivider = 1.0M;

                switch (frequencyType)
                {
                    case (int)SalaryFrequency.Monthly:
                        loanTermDivider = 1.0M;

                        break;
                    case (int)SalaryFrequency.Fortnightly:
                        loanTermDivider = 2.0M;

                        break;
                    case (int)SalaryFrequency.Weekly:
                        loanTermDivider = 4.0M;

                        break;
                }

                var funeral = vapDetailsRow.FUN_CST * (Convert.ToDecimal(loanTerm) / loanTermDivider);
                var vap = ((totalVap - funeral) / 115) * 100;
                var vat = ((totalVap - funeral) / 115) * 15;

                var totalTax = Math.Round(vat, 2);
                var totalFuneral = Math.Round(funeral, 2);
                var totalOthers = Math.Round(vap, 2);

                returnValue = new
                {
                    TotalTAX = totalTax,
                    TotalFuneralSP = totalFuneral,
                    TotalOthers = totalOthers,
                    TAXPerInstallment = Math.Round(totalTax / loanTerm, 2),
                    FuneralPerInstallment = Math.Round(totalFuneral / loanTerm, 2),
                    OthersPerInstallment = Math.Round(totalOthers / loanTerm, 2),
                    BeneficiaryAmount = vapDetailsRow.MainCVR,
                    InsuredAmount = vapDetailsRow.SpouCVR,
                    VAP_CST = vapDetailsRow.VAP_CST,
                    MaxAge = vapDetailsRow.MaxChild,
                    Age_14_21 = vapDetailsRow.Age_14_21,
                    Age_06_13 = vapDetailsRow.Age_06_13,
                    Age_01_05 = vapDetailsRow.Age_01_05,
                    Age_00_11mt = vapDetailsRow.Age_00_11mt,
                    VapAmountperInstallment = VapAmount


                };

                return returnValue;
            }
        }

        [HttpGet]
        [Route("applications/GetCreditScore/{id}")]
        public Response<VMCreditScore> GetCreditScore(int id)
        {
            try
            {
                log.Info(string.Format("Get credit score for application {0}", id));
                using (var result = new BackOfficeWebServiceClient("BackOfficeWebServer.NET"))
                {
                    var res = result.GetCreditScore(id);
                    VMCreditScore obj = new VMCreditScore()
                    {
                        Score = res,
                        //Response = JsonConvert.DeserializeObject<ROOT>(res.CreditScoreResponse)
                        Response = JObject.Parse(res.CreditScoreResponse)
                    };
                    res.CreditScoreResponse = null;
                    return Response<VMCreditScore>.CreateResponse(Constants.Success, obj, null);
                }
            }
            catch (Exception ex)
            {
                log.Error(string.Format("Get credit score for application {0}\nError: {1}", id, ex));
                return Response<VMCreditScore>.CreateResponse(Constants.Failed, null, new ErrorHandler() { ErrorCode = Constants.ApplicationErrorCode, Message = Constants.CreditScoreError });
            }
        }

        public bool SaveDebitOrderResponse(Atlas.Domain.DTO.Account.ACC_DebitOrderDTO dbtOrder)
        {
            try
            {
                log.Info(string.Format("Save Debit Order Response for account id {0}, transaction id {1}", dbtOrder.AccountId, dbtOrder.transactionID));
                using (var uow = new UnitOfWork())
                {
                    var debitOrder = new XPQuery<ACC_DebitOrder>(uow).Where(d => d.AccountId == dbtOrder.AccountId).FirstOrDefault();

                    if (debitOrder == null)
                    {
                        log.Info(string.Format("create new debit Order  for account id {0}, transaction id {1}", dbtOrder.AccountId, dbtOrder.transactionID));
                        try
                        {
                            debitOrder = new ACC_DebitOrder(uow)
                            {
                                responseCode = dbtOrder.responseCode,
                                pAN = dbtOrder.pAN,
                                transactionID = dbtOrder.transactionID,
                                approvalCode = dbtOrder.approvalCode,
                                contractAmount = dbtOrder.contractAmount,
                                accountNumber = dbtOrder.accountNumber,
                                accountType = dbtOrder.accountType,
                                tracking = dbtOrder.tracking,
                                adjRule = dbtOrder.adjRule,
                                frequency = dbtOrder.frequency,
                                CreateDate = DateTime.UtcNow,
                                AccountId = dbtOrder.AccountId,
                                ContractNumber = dbtOrder.ContractNumber
                            };
                            debitOrder.Save();
                            uow.CommitChanges();
                        }
                        catch (Exception ex)
                        {
                            log.Info(string.Format("Error creating new Debit Order for account id {0}, transaction id {1}\nError: {2}", dbtOrder.AccountId, dbtOrder.transactionID, ex));
                            throw;
                        }
                    }
                    else
                    {
                        try
                        {
                            log.Info(string.Format("update debit Order for account id {0}, transaction id {1}", dbtOrder.AccountId, dbtOrder.transactionID));

                            debitOrder.responseCode = dbtOrder.responseCode;
                            debitOrder.pAN = dbtOrder.pAN;
                            debitOrder.transactionID = dbtOrder.transactionID;
                            debitOrder.approvalCode = dbtOrder.approvalCode;
                            debitOrder.contractAmount = dbtOrder.contractAmount;
                            debitOrder.accountNumber = dbtOrder.accountNumber;
                            debitOrder.accountType = dbtOrder.accountType;
                            debitOrder.tracking = dbtOrder.tracking;
                            debitOrder.adjRule = dbtOrder.adjRule;
                            debitOrder.frequency = dbtOrder.frequency;
                            debitOrder.ContractNumber = dbtOrder.ContractNumber;
                            debitOrder.Save();
                            uow.CommitChanges();
                        }
                        catch (Exception ex)
                        {
                            log.Info(string.Format("Error updating Debit Order for account id {0}, transaction id {1}\nError: {2}", dbtOrder.AccountId, dbtOrder.transactionID, ex));
                            throw;
                        }
                    }
                    return true;
                }
            }
            catch (Exception ex)
            {
                log.Info(string.Format("Error Saving Debit Order Response for account id {0}, transaction id {1}\nError: {2}", dbtOrder.AccountId, dbtOrder.transactionID, ex));
                return false;
            }
        }

        [Route("LoanAmountValidation/{LoanAmount}")]
        public Response<string> LoanAmountValidation(decimal loanAmount)
        {
            try
            {
                log.Info(string.Format("LoanAmountValidation for loan amount {0}", loanAmount));
                using (var uow = new UnitOfWork())
                {
                    try
                    {
                        var account = new XPQuery<ACC_AccountType>(uow).FirstOrDefault(a => a.AccountTypeId == 1);
                        if (loanAmount <= account.MaxAmount && loanAmount >= account.MinAmount)
                        {
                            log.Info(string.Format("LoanAmountValidation success for loan amount {0}", loanAmount));
                            return Response<string>.CreateResponse(Constants.Success, "true", null);
                        }
                        else
                        {
                            log.Info(string.Format("LoanAmountValidation failed for loan amount {0}", loanAmount));
                            return Response<string>.CreateResponse(Constants.Failed, null, new ErrorHandler() { ErrorCode = Constants.ApplicationErrorCode, Message = string.Format(Constants.LoanAmountValidationErrorMessage, account.MinAmount, account.MaxAmount) });
                        }
                    }
                    catch (Exception ex)
                    {
                        log.Error(string.Format("Error getting ACC_AccountType details in [LoanAmountValidation] for loan amount {0}\nError: {1}", loanAmount, ex));
                        throw;
                    }
                };

            }
            catch (Exception ex)
            {
                log.Error(string.Format("Error validating loan amount {0} in [LoanAmountValidation]\nError: {1}", loanAmount, ex));
                return Response<string>.CreateResponse(Constants.Failed, null, new ErrorHandler() { ErrorCode = Constants.ApplicationErrorCode, Message = Constants.LoanAmountValidationFailedMessage });
            }
        }

        [Route("SalaryAmountValidation/{Amount}")]
        public Response<string> SalaryAmountValidation(decimal? amount)
        {
            try
            {
                log.Info(string.Format("SalaryAmountValidation for loan amount {0}", amount));
                using (var uow = new UnitOfWork())
                {
                    try
                    {
                        var account = new XPQuery<ACC_AccountType>(uow).FirstOrDefault(a => a.AccountTypeId == 1);
                        if (amount >= account.MinSalary)
                        {
                            log.Info(string.Format("SalaryAmountValidation success for amount {0}", amount));
                            return Response<string>.CreateResponse(Constants.Success, "true", null);
                        }
                        else
                        {
                            log.Info(string.Format("SalaryAmountValidation failed for amount {0}", amount));

                            return Response<string>.CreateResponse(Constants.Failed, null, new ErrorHandler() { ErrorCode = Constants.ApplicationErrorCode, Message = string.Format(Constants.SalaryAmountValidationErrorMessage, account.MinSalary) });

                        }
                    }
                    catch (Exception ex)
                    {
                        log.Error(string.Format("Error getting ACC_AccountType details in [SalaryAmountValidation] for loan amount {0}\nError: {1}", amount, ex));
                        throw;
                    }
                }
            }
            catch (Exception ex)
            {
                log.Error(string.Format("Error validating salary amount {0} in [SalaryAmountValidation]\nError: {1}", amount, ex));
                return Response<string>.CreateResponse(Constants.Failed, null, new ErrorHandler() { ErrorCode = Constants.ApplicationErrorCode, Message = Constants.SalaryAmountValidationFailedMessage });
            }
        }

        [NonAction]
        private Response<FingerEnrollmentResponse> FingerPrintEnrolled(int id)
        {
            try
            {
                log.Info(string.Format("[FingerPrintEnrolled] for person id", id));
                if (id != 0)
                {
                    using (var client = new HttpClient())
                    {


                        HttpResponseMessage response = HttpClass.GetHttpfingerprints("getenrolled", "&person_id=" + id);
                        response.EnsureSuccessStatusCode();

                        if (response.IsSuccessStatusCode)
                        {
                            var authResponse = JsonConvert.DeserializeObject<FingerEnrollmentResponse>(response.Content.ReadAsStringAsync().Result);
                            if (authResponse.fingers_enrolled.Length > 0)
                            {
                                log.Info(string.Format("[FingerPrintEnrolled] success for person id", id));
                                return Response<FingerEnrollmentResponse>.CreateResponse(Constants.Success, authResponse, null);
                            }
                            else
                            {
                                log.Info(string.Format("[FingerPrintEnrolled] failed for person id", id));
                                return Response<FingerEnrollmentResponse>.CreateResponse(Constants.Failed, null, new ErrorHandler() { ErrorCode = Constants.FingerPrintEnrollFailedCode, Message = Constants.FingerPrintEnrollFailed });
                            }
                        }
                        log.Info(string.Format("[FingerPrintEnrolled] failed for person id. API failed.", id));
                        return Response<FingerEnrollmentResponse>.CreateResponse(Constants.Failed, null, new ErrorHandler() { ErrorCode = Constants.ApplicationErrorCode, Message = Constants.FingerPrintEnrollFailed });
                    }
                }
                else
                {
                    log.Info(string.Format("[FingerPrintEnrolled] failed for person id. Person details not found.", id));
                    return Response<FingerEnrollmentResponse>.CreateResponse(Constants.Failed, null, new ErrorHandler() { ErrorCode = Constants.ApplicationErrorCode, Message = Constants.PersonIdNotFound });
                }
            }
            catch (Exception ex)
            {
                log.Error(string.Format("Error in [FingerPrintEnrolled] for person id\nError: {1}", id, ex));
                return Response<FingerEnrollmentResponse>.CreateResponse(Constants.Failed, null, new ErrorHandler() { ErrorCode = Constants.ApplicationErrorCode, Message = Constants.FPEnrollErrorMessage });
            }
        }

        [NonAction]
        private Response<MachineDetails> GetMachineDetails()
        {
            try
            {
                log.Info("Get machine details");
                string hostName = Dns.GetHostName();
                //string ip_address = Dns.GetHostByName(hostName).AddressList[0].ToString();

                //string ip_address = HttpContext.Current.Request.ServerVariables["HTTP_X_FORWARDED_FOR"];
                //if (ConfigurationManager.AppSettings["fingerPrintbypass"] == "false")
                //{
                //    if (ip_address != null || ip_address != String.Empty)
                //    {
                //        ip_address = HttpContext.Current.Request.ServerVariables["REMOTE_ADDR"];
                //    }
                //}
                //else
                //{
                //    ip_address = ConfigurationManager.AppSettings["fingerPrintIp"];
                //}

                string ip_address = GetUserLocalIP();
                //ip_address = ConfigurationManager.AppSettings["fingerPrintIp"]; //added for debugging
                using (var client = new HttpClient())
                {


                    HttpResponseMessage response = HttpClass.GetHttpfingerprints("getmachineid", "&ip_address=" + ip_address);
                    response.EnsureSuccessStatusCode();

                    if (response.IsSuccessStatusCode)
                    {
                        log.Info("Get machine details success");
                        var authResponse = JsonConvert.DeserializeObject<MachineDetails>(response.Content.ReadAsStringAsync().Result);
                        return Response<MachineDetails>.CreateResponse(Constants.Success, authResponse, null);
                    }
                    log.Info("Get machine details failed");
                    return Response<MachineDetails>.CreateResponse(Constants.Failed, null, new ErrorHandler() { ErrorCode = Constants.ApplicationErrorCode, Message = Constants.GetMachineDetailsUnavailableMessage });
                }
            }
            catch (Exception ex)
            {
                log.Error(string.Format("Get machine details success\nError: {0}", ex)); ;
                return Response<MachineDetails>.CreateResponse(Constants.Failed, null, new ErrorHandler() { ErrorCode = Constants.ApplicationErrorCode, Message = Constants.GetMachineDetailsErrorMessage });
            }

        }

        [NonAction]
        private string GetUserLocalIP()
        {
            using (var uow = new UnitOfWork())
            {
                log.Info("Get local IP");
                var bos_session = new XPQuery<BOS_Session>(uow).Where(x => x.Token == HttpContext.Current.Request.Headers["sessionToken"] & x.IsActive);
                if (bos_session != null && bos_session.Count() > 0)
                {
                    log.Info("local IP: " + bos_session.FirstOrDefault().MachineIp.ToString());
                    return bos_session.FirstOrDefault().MachineIp;
                }
                return null;
            }
        }

        [NonAction]
        public Response<FingerprintPersonId> GetPersonId(int id)
        {
            try
            {
                log.Info(string.Format("get person id for application {0}", id));
                var application = new ApplicationDto();
                using (var result = new BackOfficeWebServiceClient("BackOfficeWebServer.NET"))
                {
                    try
                    {
                        var filterConditions = FilterUtil.GetFiltersAsString(BO_Object.Applications, Convert.ToInt32(HttpContext.Current.Session["UserId"]));
                        application = result.GetApplicationDetail(filterConditions, id);
                    }
                    catch (Exception ex)
                    {
                        log.Error(string.Format("Error in fetching application {0} details\nError: {1}", id, ex));
                        throw;
                    }
                }
                if (application != null)
                {
                    using (var client = new HttpClient())
                    {


                        HttpResponseMessage response = HttpClass.GetHttpfingerprints("getpersonid", "&identity_or_passport=" + application.ApplicationClient.IDNumber);
                        response.EnsureSuccessStatusCode();

                        if (response.IsSuccessStatusCode)
                        {
                            log.Info(string.Format("get person id for application {0} success", id));
                            var authResponse = JsonConvert.DeserializeObject<FingerprintPersonId>(response.Content.ReadAsStringAsync().Result);
                            return Response<FingerprintPersonId>.CreateResponse(Constants.Success, authResponse, null);
                        }
                        log.Info(string.Format("get person id for application {0} failed. API failed", id));
                        return Response<FingerprintPersonId>.CreateResponse(Constants.Failed, null, new ErrorHandler() { ErrorCode = Constants.ApplicationErrorCode, Message = Constants.PersonDetailsUnavailableMessage });
                    }
                }
                log.Info(string.Format("get person id for application {0} failed. Application details not found.", id));
                return Response<FingerprintPersonId>.CreateResponse(Constants.Failed, null, new ErrorHandler() { ErrorCode = Constants.ApplicationErrorCode, Message = Constants.ApplicationDetailsNotFound });
            }
            catch (Exception ex)
            {
                log.Error(string.Format("error getting person id for application {0} failed\nError: {1}", id, ex));
                return Response<FingerprintPersonId>.CreateResponse(Constants.Failed, null, new ErrorHandler() { ErrorCode = Constants.ApplicationErrorCode, Message = Constants.GetPersonDetailsErrorMessage });
            }

        }

        [NonAction]
        private Response<FingerprintPersonId> AddPerson(int id)
        {
            try
            {
                log.Info(string.Format("add person for application {0}", id));
                var application = new ApplicationDto();
                using (var result = new BackOfficeWebServiceClient("BackOfficeWebServer.NET"))
                {
                    try
                    {
                        var filterConditions = FilterUtil.GetFiltersAsString(BO_Object.Applications, Convert.ToInt32(HttpContext.Current.Session["UserId"]));
                        application = result.GetApplicationDetail(filterConditions, id);
                    }
                    catch (Exception ex)
                    {
                        log.Error(string.Format("Error in fetching application {0} details\nError: {1}", id, ex));
                        throw;
                    }
                }
                if (application != null)
                {
                    //if (application.ApplicationClient.CellNo == null || application.ApplicationClient.Email == null || application.Employer.CellNo == null || application.Employer.TelephoneNo == null)
                    //{
                    //    return Response<FingerprintPersonId>.CreateResponse(Constants.Failed, null, new ErrorHandler() { ErrorCode = Constants.ApplicationErrorCode, Message = "Please update customers profile and employer details" });
                    //}
                    using (var client = new HttpClient())
                    {

                        StringBuilder url = new StringBuilder();
                        application.ApplicationClient.CellNo = application.ApplicationClient.CellNo.VerifyCellNo();
                        application.Employer.CellNo = application.Employer.CellNo.VerifyCellNo();
                        url.Append("&identity_or_passport=" + application.ApplicationClient.IDNumber + "&first_name=" + application.ApplicationClient.Firstname + "&other_names=" + "" + "&surname=" + application.ApplicationClient.Surname);
                        url.Append("&date_of_birth=" + Convert.ToDateTime(application.ApplicationClient.DateOfBirth).ToString("yyyyMMdd") + "&gender=" + application.ApplicationClient.Gender + "&cell_num=" + application.ApplicationClient.CellNo + "&email=" + application.ApplicationClient.Email + "&work_num=" + application.Employer.TelephoneNo + "&home_num=" + application.Employer.CellNo);
                        HttpResponseMessage response = HttpClass.GetHttpfingerprints("addperson", url.ToString());
                        application.ApplicationClient.CellNo = application.ApplicationClient.CellNo.VerifyCellNo();
                        response.EnsureSuccessStatusCode();

                        if (response.IsSuccessStatusCode)
                        {
                            var authResponse = JsonConvert.DeserializeObject<FingerprintPersonId>(response.Content.ReadAsStringAsync().Result);
                            if (authResponse.success)
                            {
                                log.Info(string.Format("add person for application {0} success", id));
                                return Response<FingerprintPersonId>.CreateResponse(Constants.Success, authResponse, null);
                            }
                            else
                            {
                                log.Info(string.Format("add person for application {0} failed", id));
                                return Response<FingerprintPersonId>.CreateResponse(Constants.Failed, authResponse, new ErrorHandler() { ErrorCode = Constants.ApplicationErrorCode, Message = authResponse.error });
                            }
                        }
                        log.Info(string.Format("add person for application {0} failed. API failed.", id));
                        return Response<FingerprintPersonId>.CreateResponse(Constants.Failed, null, new ErrorHandler() { ErrorCode = Constants.ApplicationErrorCode, Message = Constants.PersonDetailsUnavailableMessage });
                    }
                }
                log.Info(string.Format("add person for application {0} failed. Application details not found.", id));
                return Response<FingerprintPersonId>.CreateResponse(Constants.Failed, null, new ErrorHandler() { ErrorCode = Constants.ApplicationErrorCode, Message = Constants.ApplicationDetailsNotFound });
            }
            catch (Exception ex)
            {
                log.Error(string.Format("error adding person id for application {0} failed\nError: {1}", id, ex));
                return Response<FingerprintPersonId>.CreateResponse(Constants.Failed, null, new ErrorHandler() { ErrorCode = Constants.ApplicationErrorCode, Message = Constants.AddPersonDetailsErrorMessage });
            }
        }

        [NonAction]
        private Response<object> StartEnrolment(int id, long consultantId)
        {
            Response<MachineDetails> setting = null;
            try
            {
                log.Info(string.Format("Start Enrolment for application {0}, consultant {1}", id, consultantId));
                setting = GetMachineDetails();
                if (setting.Data.success)
                {
                    var application = new ApplicationDto();
                    using (var result = new BackOfficeWebServiceClient("BackOfficeWebServer.NET"))
                    {
                        try
                        {
                            var filterConditions = FilterUtil.GetFiltersAsString(BO_Object.Applications, Convert.ToInt32(HttpContext.Current.Session["UserId"]));
                            application = result.GetApplicationDetail(filterConditions, id);
                        }
                        catch (Exception ex)
                        {
                            log.Error(string.Format("Error in fetching application {0} details\nError: {1}", id, ex));
                            throw;
                        }
                    }
                    if (application != null)
                    {
                        using (var client = new HttpClient())
                        {

                            //application.ApplicationClient.PersonId = Convert.ToInt64(Constants.TempPersonId);
                            var getEnrol = FingerPrintEnrolled((int)application.ApplicationClient.PersonId);
                            if (getEnrol.Data == null || getEnrol.Data.fingers_enrolled.Length == 0)
                            {
                                StringBuilder url = new StringBuilder();
                                url.Append("&machine_id=" + setting.Data.machine_id + "&machine_hkey=" + setting.Data.machine_hkey + "&person_id=" + application.ApplicationClient.PersonId);
                                url.Append("&user_person_id=" + consultantId + "&caption=" + Uri.EscapeDataString("Fingerprint enrol - " + application.ApplicationClient.Firstname + " " + application.ApplicationClient.Surname) + "&body=" + Uri.EscapeDataString("Please enrol client " + application.ApplicationClient.Firstname + " " + application.ApplicationClient.Surname + "now"));
                                HttpResponseMessage response = HttpClass.GetHttpfingerprints("enrolstart", url.ToString());
                                response.EnsureSuccessStatusCode();

                                if (response.IsSuccessStatusCode)
                                {
                                    var authResponse = JsonConvert.DeserializeObject<FingerprintSession>(response.Content.ReadAsStringAsync().Result);
                                    if (authResponse.session_id != null)
                                    {
                                        log.Info(string.Format("start enrollment for application {0} and consultant {1} success", id, consultantId));
                                        return Response<object>.CreateResponse(Constants.Failed, authResponse.session_id, new ErrorHandler() { ErrorCode = Constants.FingerPrintEnrollFailedCode, Message = Constants.VerifyFingerPrint });
                                    }
                                    else
                                    {
                                        if (ConfigurationManager.AppSettings["MockByPass"] == "true")
                                        {
                                            log.Error(string.Format("[MockByPass is true. Mocking response. StartEnrolment throwing custom exception for Mock ByPass]: MockByPass:"));
                                            throw new Exception("[MockByPass:for application: " + id + " StartEnrolment throwing custom exception for Mock ByPass]: MockByPass:");
                                        }
                                        log.Info(string.Format("start enrollment for application {0} and consultant {1} failed", id, consultantId));
                                        return Response<object>.CreateResponse(Constants.Failed, null, new ErrorHandler() { ErrorCode = Constants.ApplicationErrorCode, Message = authResponse.error });
                                    }

                                    //var verifyStatus = StartEnrolmentStatus(new RequestStartEnrolmentStatus() { session_id = authResponse.session_id });
                                    //if (verifyStatus.Data.success && verifyStatus.Data.success && verifyStatus.Data.finger_ids_enrolled.Length>0)
                                    //    return Response<object>.CreateResponse(Constants.Success, verifyStatus, null);
                                    //else
                                    //    return Response<object>.CreateResponse(Constants.Failed, null, new ErrorHandler() { ErrorCode = Constants.TryAgainCode, Message = verifyStatus.Data.error });

                                }
                                log.Info(string.Format("add person for application {0} and consultant {1} failed. API failed.", id, consultantId));
                                return Response<object>.CreateResponse(Constants.Failed, null, new ErrorHandler() { ErrorCode = Constants.ApplicationErrorCode, Message = Constants.FPEnrollErrorMessage });
                            }
                            else
                            {
                                if (ConfigurationManager.AppSettings["MockByPass"] == "true")
                                {
                                    log.Error(string.Format("MockByPass is true. Mocking StartEnrolment."));
                                    return Response<object>.CreateResponse(Constants.Success, setting, null);
                                }
                                log.Info(string.Format("add person for application {0} and consultant {1} failed. Finger prints already enrolled.", id, consultantId));
                                return Response<object>.CreateResponse(Constants.Failed, null, new ErrorHandler() { ErrorCode = Constants.FingerPrintEnrollFailedCode, Message = Constants.FingerPrintAlreadyEnroll });
                            }
                        }
                    }
                    log.Info(string.Format("add person for application {0} and consultant {1} failed. Application details not found.", id, consultantId));
                    return Response<object>.CreateResponse(Constants.Failed, null, new ErrorHandler() { ErrorCode = Constants.ApplicationErrorCode, Message = Constants.ApplicationDetailsNotFound });
                }
                else
                {
                    log.Info(string.Format("add person for application {0} and consultant {1} failed. Application details not found.", id, consultantId));
                    return Response<object>.CreateResponse(Constants.Failed, null, new ErrorHandler() { ErrorCode = Constants.FPEnrollErrorMessage, Message = setting.Data.error });
                }
            }
            catch (Exception ex)
            {
                if (ConfigurationManager.AppSettings["MockByPass"] == "true")
                {
                    log.Error(string.Format("MockByPass is true. Mocking respone. Exception occured in StartEnrollment:\t {0}", ex));
                    return Response<object>.CreateResponse(Constants.Success, setting, null);
                }
                log.Error(string.Format("error adding person id for application {0} and consultant {1} failed\nError: {2}", id, consultantId, ex));
                return Response<object>.CreateResponse(Constants.Failed, null, new ErrorHandler() { ErrorCode = Constants.ApplicationErrorCode, Message = Constants.FPEnrollErrorMessage });
            }
        }

        [NonAction]
        public Response<object> VerifyEnrolment(int id, string userType, long personId)
        {
            Response<MachineDetails> setting = null;
            try
            {
                log.Info(string.Format("Verify Enrolment for application {0}, consultant {1}, usertype {2}", id, personId, userType));
                setting = GetMachineDetails();
                if (setting.Data.success)
                {
                    long pId = 0;
                    var application = new ApplicationDto();
                    using (var result = new BackOfficeWebServiceClient("BackOfficeWebServer.NET"))
                    {
                        try
                        {
                            var filterConditions = FilterUtil.GetFiltersAsString(BO_Object.Applications, Convert.ToInt32(HttpContext.Current.Session["UserId"]));
                            application = result.GetApplicationDetail(filterConditions, id);
                        }
                        catch (Exception ex)
                        {
                            log.Error(string.Format("Error in fetching application {0} details\nError: {1}", id, ex));
                            throw;
                        }
                    }
                    if (application != null)
                    {
                        if (userType.ToUpper() == "CUSTOMER")
                        {
                            if (ConfigurationManager.AppSettings["fingerPrintbypass"] == "false")
                            {
                                pId = Convert.ToInt64(application.ApplicationClient.PersonId);
                            }
                            else
                            {
                                log.Info("Mock fingerPrintbypass is true. Mock UserPersonId. userType: CUSTOMER");
                                pId = Convert.ToInt64(ConfigurationManager.AppSettings["UserPersonId"]);
                            }

                        }
                        else
                        {
                            if (ConfigurationManager.AppSettings["fingerPrintbypass"] == "false")
                            {
                                pId = Convert.ToInt64(personId);
                            }
                            else
                            {
                                log.Info("Mock fingerPrintbypass is true. Mock UserPersonId");
                                pId = Convert.ToInt64(ConfigurationManager.AppSettings["UserPersonId"]);
                            }

                        }
                        var getEnrol = FingerPrintEnrolled((int)pId);
                        if ((getEnrol.Data == null ? true : (getEnrol.Data.fingers_enrolled.Length == 0 ? true : false)))
                        {
                            log.Info(string.Format("Person not enrolled. Start enrolment for application {0}, consultant {1}, usertype {2}", id, personId, userType));
                            return StartEnrolment(id, personId);
                            //return Response<object>.CreateResponse(Constants.Failed, null, new ErrorHandler() { ErrorCode = Constants.FingerPrintEnrollFailedCode, Message = Constants.FingerPrintEnrollFailed });
                        }
                        else
                        {
                            log.Info(string.Format("Person already enrolled. Verify Enrolment for application {0}, consultant {1}, usertype {2}", id, personId, userType));
                            using (var client = new HttpClient())
                            {

                                StringBuilder url = new StringBuilder();
                                string personName = userType;
                                if (userType.ToUpper() == "CUSTOMER")
                                {
                                    personId = pId;
                                    personName = application.ApplicationClient.Firstname + " " + application.ApplicationClient.Surname;
                                }
                                url.Append("&machine_id=" + setting.Data.machine_id + "&machine_hkey=" + setting.Data.machine_hkey + "&person_id=" + personId);
                                url.Append("&security_level=7&caption=" + Uri.EscapeDataString("Fingerprint verification - " + personName) + "&body=" + Uri.EscapeDataString("Please verify client " + personName + " now"));
                                HttpResponseMessage response = HttpClass.GetHttpfingerprints("verifystart", url.ToString());
                                response.EnsureSuccessStatusCode();

                                if (response.IsSuccessStatusCode)
                                {
                                    System.Threading.Thread.Sleep(Convert.ToInt32(ConfigurationManager.AppSettings["fp_machinetime"]));
                                    var authResponse = JsonConvert.DeserializeObject<FingerprintSession>(response.Content.ReadAsStringAsync().Result);
                                    if (authResponse.success)
                                    {
                                        var verifyStatus = VerifyEnrolmentStatus(new RequestStartEnrolmentStatus() { session_id = authResponse.session_id });
                                        if (verifyStatus.Status != "Failed")
                                        {
                                            if (verifyStatus.Data.verified && verifyStatus.Data.completed)
                                            {
                                                log.Info(string.Format("Verify Enrolment success for application {0}, consultant {1}, usertype {2}", id, personId, userType));
                                                return Response<object>.CreateResponse(Constants.Success, authResponse, null);
                                            }
                                            else
                                            {
                                                if (ConfigurationManager.AppSettings["MockByPass"] == "true")
                                                {
                                                    log.Error(string.Format("[VerifyEnrolment] MockByPass is true. Mocking response. VerifyEnrolment "));
                                                    return Response<object>.CreateResponse(Constants.Success, authResponse, null);
                                                }
                                                log.Info(string.Format("Verify Enrolment for failed application {0}, consultant {1}, usertype {2}. Error: {3}", id, personId, userType, verifyStatus.Data.error));
                                                return Response<object>.CreateResponse(Constants.Failed, verifyStatus.Data, new ErrorHandler() { ErrorCode = Constants.FingerPrintEnrollFailedCode, Message = verifyStatus.Data.error });
                                            }
                                        }
                                        else
                                        {
                                            if (ConfigurationManager.AppSettings["MockByPass"] == "true")
                                            {
                                                log.Error(string.Format("[VerifyEnrolment] MockByPass is true. Mocking response. VerifyEnrolment "));
                                                return Response<object>.CreateResponse(Constants.Success, authResponse, null);
                                            }
                                            log.Info(string.Format("Verify Enrolment for failed application {0}, consultant {1}, usertype {2}. Error: {3}", id, personId, userType, verifyStatus.Error.Message));
                                            return Response<object>.CreateResponse(Constants.Failed, verifyStatus.Data, new ErrorHandler() { ErrorCode = verifyStatus.Error.ErrorCode, Message = verifyStatus.Error.Message });
                                        }
                                    }
                                    else
                                    {
                                        if (ConfigurationManager.AppSettings["MockByPass"] == "true")
                                        {
                                            log.Error(string.Format("[VerifyEnrolment] MockByPass is true. Mocking response. VerifyEnrolment "));
                                            return Response<object>.CreateResponse(Constants.Success, authResponse, null);
                                        }
                                        log.Info(string.Format("Verify Enrolment for failed application {0}, consultant {1}, usertype {2}. FingerprintSession failed.", id, personId, userType));
                                        return Response<object>.CreateResponse(Constants.Success, null, new ErrorHandler() { ErrorCode = Constants.ApplicationErrorCode, Message = authResponse.error });
                                    }
                                }
                                log.Info(string.Format("Verify Enrolment for failed application {0}, consultant {1}, usertype {2}. API failed.", id, personId, userType));
                                return Response<object>.CreateResponse(Constants.Failed, null, new ErrorHandler() { ErrorCode = Constants.FingerPrintVerifyFailedCode, Message = Constants.FPVerifyErrorMessage });
                            }
                        }
                    }
                    log.Info(string.Format("Application details not found for application {0}, consultant {1}, usertype {2}. API failed.", id, personId, userType));
                    return Response<object>.CreateResponse(Constants.Failed, null, new ErrorHandler() { ErrorCode = Constants.ApplicationErrorCode, Message = Constants.ApplicationDetailsNotFound });
                }
                else
                {
                    if (ConfigurationManager.AppSettings["MockByPass"] == "true")
                    {
                        log.Error(string.Format("[VerifyEnrolment] MockByPass is true. Mocking response. VerifyEnrolment "));
                        throw new Exception("[MockByPass:for application: " + id + " VerifyEnrolment throwing custom exception for Mock ByPass]: MockByPass:");
                    }


                    log.Info(string.Format("Verify Enrolment failed for application {0}, consultant {1}, usertype {2}, settings error {3}", id, personId, userType, setting.Data.error));
                    return Response<object>.CreateResponse(Constants.Failed, null, new ErrorHandler() { ErrorCode = Constants.FingerPrintVerifyFailedCode, Message = setting.Data.error });
                }
            }
            catch (Exception ex)
            {
                if (ConfigurationManager.AppSettings["MockByPass"] == "true")
                {
                    log.Error(string.Format("Exception in [VerifyEnrolment] MockByPass is true. Mocking response.\nException: {0}"));
                    return Response<object>.CreateResponse(Constants.Success, setting, null);
                }
                log.Info(string.Format("Verify Enrolment failed for application {0}, consultant {1}, usertype {2}\nError: {3}", id, personId, userType, ex));
                return Response<object>.CreateResponse(Constants.Failed, null, new ErrorHandler() { ErrorCode = Constants.FingerPrintVerifyFailedCode, Message = Constants.FPVerifyErrorMessage });
            }
        }

        [NonAction]
        private Response<ResponseVerifyEnrolmentStatus> VerifyEnrolmentStatus([FromBody]RequestStartEnrolmentStatus sessionDetails)
        {
            try
            {
                log.Info(string.Format("VerifyEnrolmentStatus session {0}", sessionDetails.session_id));
                var setting = GetMachineDetails();
                if (setting.Data.success)
                {
                    using (var client = new HttpClient())
                    {
                        StringBuilder url = new StringBuilder();
                        url.Append("&machine_id=" + setting.Data.machine_id + "&machine_hkey=" + setting.Data.machine_hkey + "&session_id=" + sessionDetails.session_id);
                        HttpResponseMessage response = HttpClass.GetHttpfingerprints("verifycheck", url.ToString());
                        response.EnsureSuccessStatusCode();

                        if (response.IsSuccessStatusCode)
                        {
                            var authResponse = JsonConvert.DeserializeObject<ResponseVerifyEnrolmentStatus>(response.Content.ReadAsStringAsync().Result);
                            if (ConfigurationManager.AppSettings["fingerPrintbypass"] == "true")
                            {
                                log.Info(string.Format("Mock fingerPrintbypass is true. Mocking verified and completed flags to true."));
                                authResponse.verified = true;
                                authResponse.completed = true;
                            }
                            if (authResponse.verified && authResponse.completed)
                            {
                                log.Info(string.Format("VerifyEnrolmentStatus success. session {0}, authResponse.verified {1}, authResponse.completed: {2}", sessionDetails.session_id, authResponse.verified, authResponse.completed));
                                return Response<ResponseVerifyEnrolmentStatus>.CreateResponse(Constants.Success, authResponse, null);
                            }
                            else
                            {
                                if (!authResponse.completed && fpVerificationCount < Convert.ToInt32(ConfigurationManager.AppSettings["FPVerificationRetryCount"]))
                                {
                                    fpVerificationCount++;
                                    log.Info(string.Format("retry {0} VerifyEnrolmentStatus check", fpVerificationCount));
                                    System.Threading.Thread.Sleep(10000);
                                    VerifyEnrolmentStatus(sessionDetails);
                                }
                                log.Info(string.Format("VerifyEnrolmentStatus failed. session {0}. Status: {1}", sessionDetails.session_id, authResponse.verified.ToString()));
                                return Response<ResponseVerifyEnrolmentStatus>.CreateResponse(Constants.Failed, authResponse, new ErrorHandler() { ErrorCode = authResponse.verified.ToString(), Message = authResponse.verified.ToString() });
                            }
                        }
                        log.Info(string.Format("VerifyEnrolmentStatus failed. session {0}. API failed.", sessionDetails.session_id));
                        return Response<ResponseVerifyEnrolmentStatus>.CreateResponse(Constants.Failed, null, new ErrorHandler() { ErrorCode = Constants.FingerPrintVerifyFailedCode, Message = Constants.FPVerifyErrorMessage });
                    }
                }
                else
                {
                    log.Info(string.Format("VerifyEnrolmentStatus failed. session {0}. setting error {1}", sessionDetails.session_id, setting.Data.error));
                    return Response<ResponseVerifyEnrolmentStatus>.CreateResponse(Constants.Failed, null, new ErrorHandler() { ErrorCode = Constants.FingerPrintVerifyFailedCode, Message = setting.Data.error });
                }
            }
            catch (Exception ex)
            {
                log.Info(string.Format("VerifyEnrolmentStatus failed. session {0}\nError: {1}", sessionDetails.session_id, ex));
                return Response<ResponseVerifyEnrolmentStatus>.CreateResponse(Constants.Failed, null, new ErrorHandler() { ErrorCode = Constants.FingerPrintVerifyFailedCode, Message = Constants.FPVerifyErrorMessage });
            }

        }

        private Atlas.ThirdParty.NuPay.Models.IssueCardResponse Check_NuCardStatus(string nuCard_Number)
        {
            try
            {
                log.Info(string.Format("Check NuCard Status for card {0}", nuCard_Number));
                Atlas.ThirdParty.NuPay.Models.NuCardDetails nuCardDetails = new Atlas.ThirdParty.NuPay.Models.NuCardDetails()
                {
                    userId = Convert.ToString(ConfigurationManager.AppSettings["userid"]),
                    userPassword = Convert.ToString(ConfigurationManager.AppSettings["password"]),
                    terminalID = Convert.ToString(ConfigurationManager.AppSettings["terminal"]),
                    profileNumber = Convert.ToString(ConfigurationManager.AppSettings["profile"]),
                    voucherNumber = nuCard_Number
                };
                var cardStatus = new Atlas.ThirdParty.NuPay.Integrations.NuCardIntegration().NuCardStatus(nuCardDetails);
                return cardStatus;
            }
            catch (Exception ex)
            {
                log.Error(string.Format("Error in Check NuCard Status for card {0}\nError: {1}", nuCard_Number, ex));
                throw;
            }
        }

        #region Stand Alone EMI Term Calculation
        [HttpPost]
        [Route("applications/StandAlonePossibleEMITermCalculation")]
        public Response<List<dynamic>> GetStandAloneEMITermCalculation(VMStandAlonePossibleLoanTermCalculation loanTermCalculation)
        {
            loanTermCalculation.applicationId = 0;
            try
            {
                log.Info(string.Format("Get EMI terms for application {0}, loan amount {1}, discretion {2}, frequency type {3}, VAP {4}, Insurance {5}, loan date {6}", loanTermCalculation.applicationId, loanTermCalculation.loanAmount, loanTermCalculation.DiscretionAmount, loanTermCalculation.frequencyTypeId, loanTermCalculation.IsVAPChecked, loanTermCalculation.IsInsuranceRequired, loanTermCalculation.LoanDate));
                //int[] period = { 1, 2, 3, 4, 5, 6, 12 };
                int periodLength = 1;
                if (loanTermCalculation.frequencyTypeId == (int)SalaryFrequency.Monthly)
                    periodLength = 12;
                if (loanTermCalculation.frequencyTypeId == (int)SalaryFrequency.Fortnightly)
                    periodLength = 26;
                if (loanTermCalculation.frequencyTypeId == (int)SalaryFrequency.Weekly)
                    periodLength = 52;
                int[] period = new int[periodLength];
                for (int i = 1; i <= periodLength; i++)
                {
                    period[i - 1] = i;
                }
                var loanDate = string.IsNullOrEmpty(loanTermCalculation.LoanDate) ? System.DateTime.UtcNow : Convert.ToDateTime(loanTermCalculation.LoanDate);
                List<dynamic> lst = new List<dynamic>();
                int annualRateOfIntrest = loanTermCalculation.RateofInterest;
                int payDay = 0;
                using (var result = new BackOfficeWebServiceClient("BackOfficeWebServer.NET"))
                {
                    try
                    {
                        var filterConditions = FilterUtil.GetFiltersAsString(BO_Object.Applications, Convert.ToInt32(HttpContext.Current.Session["UserId"]));
                        ApplicationDto applicationDetails = null;
                        payDay = applicationDetails != null ?
                            applicationDetails.Employer != null ? applicationDetails.Employer.PayDay : 0 : 0;
                    }
                    catch (Exception ex)
                    {
                        log.Error(string.Format("Error in fetching application {0} details\nError: {1}", loanTermCalculation.applicationId, ex));
                        throw;
                    }
                }
                foreach (int term in period)
                {
                    var endDate = loanDate;
                    if (loanTermCalculation.frequencyTypeId == (int)SalaryFrequency.Monthly)
                        endDate = endDate.AddMonths(term);
                    if (loanTermCalculation.frequencyTypeId == (int)SalaryFrequency.Fortnightly)
                        endDate = endDate.AddDays(14 * term);
                    if (loanTermCalculation.frequencyTypeId == (int)SalaryFrequency.Weekly)
                        endDate = endDate.AddDays(7 * term);
                    decimal costOfEMI = 0.25M;
                    decimal loanAmount = 0M;
                    // decimal calculatedEMI = 0M;
                    dynamic calculatedEMI = null;
                    loanAmount = term == 1 ? (loanTermCalculation.loanAmount * term) * (1 - costOfEMI) : lst[0].TotalInstallment * term;
                    decimal diff = 0M;

                    try
                    {
                        calculatedEMI = GetCalculatedObject(loanAmount, loanDate, endDate, loanTermCalculation.DiscretionAmount, term, loanTermCalculation.frequencyTypeId, loanTermCalculation.applicationId, loanTermCalculation.IsVAPChecked, loanTermCalculation.IsInsuranceRequired, annualRateOfIntrest, payDay, null);
                    }
                    catch (Exception ex)
                    {
                        log.Error(string.Format("[GetPossibleEMIermCalculation] Error in [GetCalculatedObject]. Error {0}", ex));
                        throw;
                    }

                    if ((loanTermCalculation.loanAmount - calculatedEMI.TotalInstallment) > 1)
                    {
                        diff = loanAmount + ((loanTermCalculation.loanAmount - calculatedEMI.TotalInstallment) - 0);
                    }
                    else
                    {
                        diff = loanAmount + ((loanTermCalculation.loanAmount - calculatedEMI.TotalInstallment));
                    }
                    do
                    {
                        try
                        {
                            calculatedEMI = GetCalculatedObject(diff, loanDate, endDate, loanTermCalculation.DiscretionAmount, term, loanTermCalculation.frequencyTypeId, loanTermCalculation.applicationId, loanTermCalculation.IsVAPChecked, loanTermCalculation.IsInsuranceRequired, annualRateOfIntrest, payDay, null);

                            if (calculatedEMI.TotalInstallment < loanTermCalculation.loanAmount)
                            {
                                diff = diff + 2;
                            }
                            else if (calculatedEMI.TotalInstallment > loanTermCalculation.loanAmount)
                            {
                                diff = diff - 1;
                            }
                        }
                        catch (Exception ex)
                        {
                            log.Error(string.Format("[GetPossibleEMIermCalculation] Error adjusting calculated EMI. Error {0}", ex));
                            throw;
                        }

                    } while ((loanTermCalculation.loanAmount - calculatedEMI.TotalInstallment) < 0);

                    using (var uow = new UnitOfWork())
                    {
                        try
                        {
                            var accounttype = new XPQuery<ACC_AccountType>(uow).FirstOrDefault(a => a.AccountTypeId == 1);
                            //if (calculatedEMI.Surplus >= 0 && calculatedEMI.LoanAmount <= accounttype.MaxAmount && calculatedEMI.LoanAmount > 0)
                            //{
                            lst.Add(calculatedEMI);
                            //}
                        }
                        catch (Exception ex)
                        {
                            log.Error(string.Format("[GetPossibleEMIermCalculation] Error getting account type. Error {0}", ex));
                            throw;
                        }
                    }
                }
                if (lst.Count() == 0)
                {
                    log.Info("[GetPossibleEMIermCalculation] list is empty");
                    return Response<List<dynamic>>.CreateResponse(Constants.Failed, lst, new ErrorHandler() { ErrorCode = Constants.ApplicationErrorCode, Message = Constants.LoanCalculationError });
                }
                log.Info(string.Format("[GetPossibleEMIermCalculation] EMI count: {0}", lst.Count()));
                return Response<List<dynamic>>.CreateResponse(Constants.Success, lst, new ErrorHandler() { });
            }
            catch (Exception ex)
            {
                log.Error(string.Format("Get EMI terms for application {0}, loan amount {1}, discretion {2}, frequency type {3}, VAP {4}, Insurance {5}, loan date {6}\nError: {7}", loanTermCalculation.applicationId, loanTermCalculation.loanAmount, loanTermCalculation.DiscretionAmount, loanTermCalculation.frequencyTypeId, loanTermCalculation.IsVAPChecked, loanTermCalculation.IsInsuranceRequired, loanTermCalculation.LoanDate, ex));
                return Response<List<dynamic>>.CreateResponse(Constants.Failed, null, new ErrorHandler() { ErrorCode = Constants.ApplicationErrorCode, Message = Constants.EMICalculationErrorMessage });
            }
        }
        #endregion


        [HttpPost]
        [Route("applications/PossibleEMITermCalculation")]
        public Response<List<dynamic>> GetPossibleEMIermCalculation(VMPossibleLoanTermCalculation loanTermCalculation)
        {
            try
            {
                log.Info(string.Format("Get EMI terms for application {0}, loan amount {1}, discretion {2}, frequency type {3}, VAP {4}, Insurance {5}, loan date {6}", loanTermCalculation.applicationId, loanTermCalculation.loanAmount, loanTermCalculation.DiscretionAmount, loanTermCalculation.frequencyTypeId, loanTermCalculation.IsVAPChecked, loanTermCalculation.IsInsuranceRequired, loanTermCalculation.LoanDate));
                //int[] period = { 1, 2, 3, 4, 5, 6, 12 };
                int periodLength = 1;
                if (loanTermCalculation.frequencyTypeId == (int)SalaryFrequency.Monthly)
                    periodLength = 12;
                if (loanTermCalculation.frequencyTypeId == (int)SalaryFrequency.Fortnightly)
                    periodLength = 26;
                if (loanTermCalculation.frequencyTypeId == (int)SalaryFrequency.Weekly)
                    periodLength = 52;
                int[] period = new int[periodLength];
                for (int i = 1; i <= periodLength; i++)
                {
                    period[i - 1] = i;
                }
                var loanDate = string.IsNullOrEmpty(loanTermCalculation.LoanDate) ? System.DateTime.UtcNow : Convert.ToDateTime(loanTermCalculation.LoanDate);
                List<dynamic> lst = new List<dynamic>();
                int annualRateOfIntrest = DBManager.GetInterestRate(loanTermCalculation.applicationId, loanDate);
                int payDay = 0;
                long? branchId = 0;
                using (var result = new BackOfficeWebServiceClient("BackOfficeWebServer.NET"))
                {
                    try
                    {
                        var filterConditions = FilterUtil.GetFiltersAsString(BO_Object.Applications, Convert.ToInt32(HttpContext.Current.Session["UserId"]));
                        ApplicationDto applicationDetails = result.GetApplicationDetail(filterConditions, loanTermCalculation.applicationId);
                        payDay = applicationDetails != null ?
                            applicationDetails.Employer != null ? applicationDetails.Employer.PayDay : 0 : 0;
                        branchId = applicationDetails.ApplicationClient.Branch.BranchId;
                    }
                    catch (Exception ex)
                    {
                        log.Error(string.Format("Error in fetching application {0} details\nError: {1}", loanTermCalculation.applicationId, ex));
                        throw;
                    }
                }
                foreach (int term in period)
                {
                    var endDate = loanDate;
                    if (loanTermCalculation.frequencyTypeId == (int)SalaryFrequency.Monthly)
                        endDate = endDate.AddMonths(term);
                    if (loanTermCalculation.frequencyTypeId == (int)SalaryFrequency.Fortnightly)
                        endDate = endDate.AddDays(14 * term);
                    if (loanTermCalculation.frequencyTypeId == (int)SalaryFrequency.Weekly)
                        endDate = endDate.AddDays(7 * term);
                    decimal costOfEMI = 0.25M;
                    decimal loanAmount = 0M;
                    // decimal calculatedEMI = 0M;
                    dynamic calculatedEMI = null;
                    loanAmount = term == 1 ? (loanTermCalculation.loanAmount * term) * (1 - costOfEMI) : lst[0].TotalInstallment * term;
                    decimal diff = 0M;

                    try
                    {
                        calculatedEMI = GetCalculatedObject(loanAmount, loanDate, endDate, loanTermCalculation.DiscretionAmount, term, loanTermCalculation.frequencyTypeId, loanTermCalculation.applicationId, loanTermCalculation.IsVAPChecked, loanTermCalculation.IsInsuranceRequired, annualRateOfIntrest, payDay, branchId);
                    }
                    catch (Exception ex)
                    {
                        log.Error(string.Format("[GetPossibleEMIermCalculation] Error in [GetCalculatedObject]. Error {0}", ex));
                        throw;
                    }

                    if ((loanTermCalculation.loanAmount - calculatedEMI.TotalInstallment) > 1)
                    {
                        diff = loanAmount + ((loanTermCalculation.loanAmount - calculatedEMI.TotalInstallment) - 0);
                    }
                    else
                    {
                        diff = loanAmount + ((loanTermCalculation.loanAmount - calculatedEMI.TotalInstallment));
                    }
                    do
                    {
                        try
                        {
                            calculatedEMI = GetCalculatedObject(diff, loanDate, endDate, loanTermCalculation.DiscretionAmount, term, loanTermCalculation.frequencyTypeId, loanTermCalculation.applicationId, loanTermCalculation.IsVAPChecked, loanTermCalculation.IsInsuranceRequired, annualRateOfIntrest, payDay, branchId);

                            if (calculatedEMI.TotalInstallment < loanTermCalculation.loanAmount)
                            {
                                diff = diff + 2;
                            }
                            else if (calculatedEMI.TotalInstallment > loanTermCalculation.loanAmount)
                            {
                                diff = diff - 1;
                            }
                        }
                        catch (Exception ex)
                        {
                            log.Error(string.Format("[GetPossibleEMIermCalculation] Error adjusting calculated EMI. Error {0}", ex));
                            throw;
                        }

                    } while ((loanTermCalculation.loanAmount - calculatedEMI.TotalInstallment) < 0);

                    using (var uow = new UnitOfWork())
                    {
                        try
                        {
                            var accounttype = new XPQuery<ACC_AccountType>(uow).FirstOrDefault(a => a.AccountTypeId == 1);
                            if (calculatedEMI.Surplus >= 0 && calculatedEMI.LoanAmount <= accounttype.MaxAmount && calculatedEMI.LoanAmount > 0)
                            {
                                lst.Add(calculatedEMI);
                            }
                        }
                        catch (Exception ex)
                        {
                            log.Error(string.Format("[GetPossibleEMIermCalculation] Error getting account type. Error {0}", ex));
                            throw;
                        }
                    }
                }
                if (lst.Count() == 0)
                {
                    log.Info("[GetPossibleEMIermCalculation] list is empty");
                    return Response<List<dynamic>>.CreateResponse(Constants.Failed, lst, new ErrorHandler() { ErrorCode = Constants.ApplicationErrorCode, Message = Constants.LoanCalculationError });
                }
                log.Info(string.Format("[GetPossibleEMIermCalculation] EMI count: {0}", lst.Count()));
                return Response<List<dynamic>>.CreateResponse(Constants.Success, lst, new ErrorHandler() { });
            }
            catch (Exception ex)
            {
                log.Error(string.Format("Get EMI terms for application {0}, loan amount {1}, discretion {2}, frequency type {3}, VAP {4}, Insurance {5}, loan date {6}\nError: {7}", loanTermCalculation.applicationId, loanTermCalculation.loanAmount, loanTermCalculation.DiscretionAmount, loanTermCalculation.frequencyTypeId, loanTermCalculation.IsVAPChecked, loanTermCalculation.IsInsuranceRequired, loanTermCalculation.LoanDate, ex));
                return Response<List<dynamic>>.CreateResponse(Constants.Failed, null, new ErrorHandler() { ErrorCode = Constants.ApplicationErrorCode, Message = Constants.EMICalculationErrorMessage });
            }
        }



        private static List<VMApplication> GetAllApplication(List<VMApplication> _appList, int page, int pageSize, out Pagination Pagination)
        {
            try
            {
                log.Info("Pagination");
                pageSize = pageSize == 0 ? Convert.ToInt32(ConfigurationManager.AppSettings["pageSize"]) : pageSize;
                page = page == 0 ? 1 : page;

                Pagination = new Pagination() { TotalNoOfData = _appList.Count(), PageNo = page, PageSize = pageSize };
                _appList = _appList.Skip((page - 1) * pageSize).Take(pageSize).ToList();
                return _appList;
            }
            catch (Exception ex)
            {
                log.Error(string.Format("Error in Pagination\nError: {0}", ex));
                throw;
            }
        }

        private long CreateCustomer(VMCustomerDetails client)
        {
            try
            {
                CustomersController ctrlCustomer = new CustomersController();
                log.Info(string.Format("Creating new customer : {0}", client.CategoryList[0].Profile.Client.IDNumber));
                Response<long> addCustomer = ctrlCustomer.CreateClient(client.CategoryList[0].Profile);
                if (addCustomer.Data > 0)
                {
                    log.Info(string.Format("Customer {0} created successfully with ID : {1}", client.CategoryList[0].Profile.Client.IDNumber, addCustomer.Data));
                    return addCustomer.Data;
                }
                log.Info(string.Format("Error while creating creating new customer {0}", client.CategoryList[0].Profile.Client.IDNumber));
                return 0;
            }
            catch (Exception ex)
            {
                log.Error(string.Format("Exception while creating new customer : ", ex.ToString()));
                return 0;
            }
        }

        public bool IsChecklistComplete(int appId)
        {
            try
            {
                log.Info(string.Format("checking application {0} completion status", appId));
                using (var result = new BackOfficeWebServiceClient("BackOfficeWebServer.NET"))
                {
                    var checklist = result.GetApplicationChecklist(appId)?.ToList();
                    if (checklist != null)
                    {
                        var count = checklist.Where(s => s.ChecklistStatus.Description.ToLower() == "pending").Count();
                        if (count == 0)
                            return true;
                    }
                }
                return false;
            }
            catch (Exception ex)
            {
                log.Error(string.Format("Error in checking application {0} completion status.\nError: ", appId, ex.ToString()));
                return false;
            }
        }


        [NonAction]
        private static decimal CalculateInitiationFeeAndVAT(decimal loanAmount, ref decimal VAT)
        {
            try
            {
                log.Info(string.Format("Calculate initiation fee for loan amount {0}", loanAmount));
                decimal[] initiationFee = new decimal[3];

                initiationFee[0] = loanAmount > 1000 ? (decimal)(1000 * (16.5 / 100)) + ((loanAmount - 1000) * 10 / 100) : (decimal)(1000 * (16.5 / 100));
                initiationFee[1] = 1050;
                initiationFee[2] = loanAmount * 15 / 100;
                VAT = (initiationFee.Min() * 15 / 100);

                return initiationFee.Min();
            }
            catch (Exception ex)
            {
                log.Error(string.Format("Error calculating initiation fee for loan amount {0}\nError: {1}", loanAmount, ex));
                throw;
            }
        }

        [NonAction]
        private decimal CalculateServiceFeesAndVAT(DateTime startDate, DateTime endDate, ref decimal VAT, decimal quantityInstallments = 1, int annualRateOfIntrest = 60)
        {
            try
            {
                log.Info(string.Format("Calculate Service fee for start date {0}, end date {1}, instalments {2}, interest {3}", startDate, endDate, quantityInstallments, annualRateOfIntrest));
                int startDay = startDate.Day;
                int endDay = endDate.Day;
                int months = endDate.Month - startDate.Month;
                if (endDate.Month <= startDate.Month && endDate.Year > startDate.Year)
                    months = 12 + months;
                int initialMonthDays = DateTime.DaysInMonth(startDate.Year, startDate.Month) - startDay + 1;
                decimal r60 = annualRateOfIntrest * initialMonthDays / 30;

                for (int i = 0; i < months; i++)
                    r60 += annualRateOfIntrest;

                VAT = (r60 / quantityInstallments) * 15 / 100;
                return Math.Round(r60 / quantityInstallments, 2);
            }
            catch (Exception ex)
            {
                log.Error(string.Format("Calculate Service fee for start date {0}, end date {1}, instalments {2}, interest {3}\nError: {4}", startDate, endDate, quantityInstallments, annualRateOfIntrest, ex));
                throw;
            }
        }
        [NonAction]
        private HttpResponseMessage GetAgreePdf(int id, bool otp = false, string fileName = "QUOTE.snx", string DocCopy = "Client")
        {
            try
            {
                ApplicationDto application = new ApplicationDto();
                AtlasCompanyDataDTO _atlas = new AtlasCompanyDataDTO();
                AutoMapper.Mapper.CreateMap<BOS_AtlasCompanyData, AtlasCompanyDataDTO>();
                using (var result = new BackOfficeWebServiceClient("BackOfficeWebServer.NET"))
                {
                    try
                    {

                        //decimal _vatAmount = 0;
                        var filterConditions = FilterUtil.GetFiltersAsString(BO_Object.Applications, Convert.ToInt32(HttpContext.Current.Session["UserId"]));
                        application = result.GetApplicationDetail(filterConditions, id);
                        SalaryFrequency frequency = (SalaryFrequency)application.Employer.SalaryType;
                        application.PaymentFrequency = frequency.ToString();
                        application.RateOfIntrest = application.Quotation.InterestRate * 12;
                        //var obj = GetCalculatedObject(application.Quotation.LoanAmount, Convert.ToDateTime(application.Quotation.LoanDate), application.Quotation.RepaymentDate, application.Affordability.Discretion, Convert.ToInt32(application.Quotation.QuantityInstallments), application.Employer.SalaryType, application.ApplicationId, application.Quotation.VAP, application.Quotation.LifeInsurance, Convert.ToInt32(application.RateOfIntrest), application.Employer.PayDay, application.BranchId);
                        application.DocCopy = DocCopy;

                        //application.Quotation.TotalInterestRate = obj.TotalInterest;
                        //application.Quotation.InitiationFeeAmount = CalculateInitiationFeeAndVAT(application.Quotation.LoanAmount, ref _vatAmount);
                        //application.Quotation.InitiationVATAmount = _vatAmount;
                        //application.Quotation.ServiceFeeAmount = obj.ServiceFeeAmount;
                        //application.Quotation.ServiceVATAmount = obj.ServiceVATAmount;

                        using (var uow = new UnitOfWork())
                        {
                            var branch = new XPQuery<BRN_Branch>(uow).Where(brn => brn.BranchId == application.BranchId).FirstOrDefault();
                            var _atlasdata = branch != null
                                 ? new XPQuery<BOS_AtlasCompanyData>(uow).Where(company => company.BRN_Num == branch.LegacyBranchNum).FirstOrDefault() : null;
                            if (_atlasdata != null)
                            {
                                _atlas = AutoMapper.Mapper.Map<BOS_AtlasCompanyData, AtlasCompanyDataDTO>(_atlasdata);
                            }
                            application.AtlasComapny = _atlas;
                        }

                    }
                    catch (Exception ex)
                    {
                        log.Info(string.Format("Error getting application {0} details during print quote\nError: {1}", id, ex));
                        throw;
                    }
                }

                if (application != null && application.Quotation != null)
                {
                    try
                    {

                        if (application != null && application.ApplicationClient != null && application.ApplicationClient.Branch != null && application.ApplicationClient.Branch.BranchId != null && application.ApplicationClient.Branch.BranchId != 0)
                        {
                            using (var uoW = new UnitOfWork())
                            {
                                var branchDetails = new XPQuery<BRN_Branch>(uoW).Where(x => x.BranchId == application.ApplicationClient.Branch.BranchId).FirstOrDefault();
                                if (branchDetails != null)
                                {
                                    application.ApplicationClient.Branch.BranchName = branchDetails.BranchName;
                                    application.AtlasComapny.BRN_Num = branchDetails.LegacyBranchNum;
                                    application.AtlasComapny.Credit_Provider_ShopNo = branchDetails.BranchName;
                                }
                                else
                                {
                                    if (application.ApplicationClient.Branch.BranchName == null) application.ApplicationClient.Branch.BranchName = "";
                                    if (application.AtlasComapny.BRN_Num == null) application.AtlasComapny.BRN_Num = "";
                                    if (application.AtlasComapny.Credit_Provider_ShopNo == null) application.AtlasComapny.Credit_Provider_ShopNo = "";
                                }
                            }
                        }


                        SnapDocumentServer server = new SnapDocumentServer();
                        server.LoadDocument(HttpContext.Current.Server.MapPath("~/Integrations/" + fileName));
                        server.Document.DataSource = application;
                        FileStream stream;
                        if (fileName == "QUOTE.snx")
                        {
                            server.ExportDocument(HttpContext.Current.Server.MapPath("~/Content/Quotations/_Quota" + id + "_" + DocCopy + ".pdf"), SnapDocumentFormat.Pdf);
                            stream = File.OpenRead(HttpContext.Current.Server.MapPath("~/Content/Quotations/_Quota" + id + "_" + DocCopy + ".pdf"));
                        }
                        else
                        {
                            server.ExportDocument(HttpContext.Current.Server.MapPath("~/Content/Quotations/_Agree" + id + "_" + DocCopy + ".pdf"), SnapDocumentFormat.Pdf);
                            stream = File.OpenRead(HttpContext.Current.Server.MapPath("~/Content/Quotations/_Agree" + id + "_" + DocCopy + ".pdf"));
                        }
                        byte[] fileBytes = new byte[stream.Length];
                        String s = Convert.ToBase64String(fileBytes);
                        stream.Read(fileBytes, 0, fileBytes.Length);
                        stream.Close();
                        var result = new HttpResponseMessage(HttpStatusCode.OK)
                        {
                            Content = new ByteArrayContent(fileBytes)
                        };
                        result.Content.Headers.ContentDisposition = new System.Net.Http.Headers.ContentDispositionHeaderValue(System.Net.Mime.DispositionTypeNames.Inline)
                        {
                            FileName = "file.pdf"
                        };
                        result.Content.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
                        if (otp == true)
                            GenerateOTP(BO_Object.Applications.ToString(), id, BO_ObjectAPI.quotation.ToString().ToLower(), application.Quotation.QuotationId);
                        return result;

                    }
                    catch (Exception ex)
                    {
                        log.Info(string.Format("Error generating PDF file\nError: {0}", id, ex));
                        throw;
                    }
                }
                else
                {
                    log.Info(string.Format("details not found application {0} details while loading NuCard", id));
                    return new HttpResponseMessage(HttpStatusCode.InternalServerError);
                }
            }
            catch (Exception ex)
            {
                log.Info(string.Format("Error generating PDF for application {0}\nError: {1}", id, ex));
                return new HttpResponseMessage(HttpStatusCode.InternalServerError);
            }
        }

        //[HttpGet]
        //[Route("applications/GetContractPdf/{id}/{fileName}")]
        [NonAction]
        public HttpResponseMessage GetContractPdf(int id, string fileName = "INS_RULE.snx", bool otp = false, string DocCopy = null)
        {
            try
            {



                BOS_CreditLifeDto _creditLifeDto = new BOS_CreditLifeDto();
                AtlasCompanyDataDTO _atlas = new AtlasCompanyDataDTO();
                AutoMapper.Mapper.CreateMap<BOS_AtlasCompanyData, AtlasCompanyDataDTO>();
                AutoMapper.Mapper.CreateMap<BOS_CreditLife, BOS_CreditLifeDto>();
                AutoMapper.Mapper.CreateMap<Atlas.Online.Data.Models.Definitions.Occupation, OccupationDto>();
                using (var result = new BackOfficeWebServiceClient("BackOfficeWebServer.NET"))
                {
                    try
                    {
                        //decimal _vatAmount = 0;
                        var filterConditions = FilterUtil.GetFiltersAsString(BO_Object.Applications, Convert.ToInt32(HttpContext.Current.Session["UserId"]));

                        using (var uow = new UnitOfWork())
                        {
                            var _creditLife = new XPQuery<BOS_CreditLife>(uow).FirstOrDefault();
                            _creditLifeDto = AutoMapper.Mapper.Map<BOS_CreditLife, BOS_CreditLifeDto>(_creditLife);
                        }

                        _creditLifeDto.application = result.GetApplicationDetail(filterConditions, id);
                        SalaryFrequency frequency = (SalaryFrequency)_creditLifeDto.application.Employer.SalaryType;
                        _creditLifeDto.application.PaymentFrequency = frequency.ToString();
                        _creditLifeDto.application.RateOfIntrest = _creditLifeDto.application.Quotation.InterestRate * 12;
                        //var obj = GetCalculatedObject(_creditLifeDto.application.Quotation.LoanAmount, Convert.ToDateTime(_creditLifeDto.application.Quotation.LoanDate), _creditLifeDto.application.Quotation.RepaymentDate, _creditLifeDto.application.Affordability.Discretion, Convert.ToInt32(_creditLifeDto.application.Quotation.QuantityInstallments), _creditLifeDto.application.Employer.SalaryType, _creditLifeDto.application.ApplicationId, _creditLifeDto.application.Quotation.VAP, _creditLifeDto.application.Quotation.LifeInsurance, Convert.ToInt32(_creditLifeDto.application.RateOfIntrest), _creditLifeDto.application.Employer.PayDay, _creditLifeDto.application.BranchId);
                        _creditLifeDto.application.DocCopy = DocCopy;

                        //_creditLifeDto.application.Quotation.TotalInterestRate = obj.TotalInterest;
                        //_creditLifeDto.application.Quotation.InitiationFeeAmount = CalculateInitiationFeeAndVAT(_creditLifeDto.application.Quotation.LoanAmount, ref _vatAmount);
                        //_creditLifeDto.application.Quotation.InitiationVATAmount = _vatAmount;
                        //_creditLifeDto.application.Quotation.ServiceFeeAmount = obj.ServiceFeeAmount;
                        //_creditLifeDto.application.Quotation.ServiceVATAmount = obj.ServiceVATAmount;

                        using (var uow = new UnitOfWork())
                        {
                            var branch = new XPQuery<BRN_Branch>(uow).Where(brn => brn.BranchId == _creditLifeDto.application.BranchId).FirstOrDefault();
                            var _atlasdata = new XPQuery<BOS_AtlasCompanyData>(uow).Where(company => company.BRN_Num == branch.LegacyBranchNum).FirstOrDefault();
                            _atlas = AutoMapper.Mapper.Map<BOS_AtlasCompanyData, AtlasCompanyDataDTO>(_atlasdata);
                            _creditLifeDto.application.AtlasComapny = _atlas;
                        }

                        if (_creditLifeDto.application.Employer != null && _creditLifeDto.application.Employer.OccupationCode != null)
                        {
                            _creditLifeDto.Occupation = new OccupationDto();
                            _creditLifeDto.Occupation.Description = DBManager.GetOccupation(_creditLifeDto.application.Employer.OccupationCode);
                        }

                        if (_creditLifeDto != null && _creditLifeDto.application != null && _creditLifeDto.application.ApplicationClient != null && _creditLifeDto.application.ApplicationClient.Branch != null && _creditLifeDto.application.ApplicationClient.Branch.BranchId != null && _creditLifeDto.application.ApplicationClient.Branch.BranchId != 0)
                        {
                            using (var uoW = new UnitOfWork())
                            {
                                var branchDetails = new XPQuery<BRN_Branch>(uoW).Where(x => x.BranchId == _creditLifeDto.application.ApplicationClient.Branch.BranchId).FirstOrDefault();
                                if (branchDetails != null)
                                {
                                    _creditLifeDto.application.ApplicationClient.Branch.BranchName = branchDetails.BranchName;
                                    _creditLifeDto.application.AtlasComapny.BRN_Num = branchDetails.LegacyBranchNum;
                                    _creditLifeDto.application.AtlasComapny.Credit_Provider_ShopNo = branchDetails.BranchName;
                                }
                                else
                                {
                                    if (_creditLifeDto.application.ApplicationClient.Branch.BranchName == null) _creditLifeDto.application.ApplicationClient.Branch.BranchName = "";
                                    if (_creditLifeDto.application.AtlasComapny.BRN_Num == null) _creditLifeDto.application.AtlasComapny.BRN_Num = "";
                                    if (_creditLifeDto.application.AtlasComapny.Credit_Provider_ShopNo == null) _creditLifeDto.application.AtlasComapny.Credit_Provider_ShopNo = "";
                                }
                            }
                        }

                        if (_creditLifeDto.application.Quotation.VAP)
                        {
                            var FirstRepayDate = Convert.ToDateTime(_creditLifeDto.application.Quotation.LoanDate);
                            decimal VAPAmountPerInstallment = _creditLifeDto.application.Quotation.VAPAmount / Convert.ToInt32(_creditLifeDto.application.Quotation.QuantityInstallments);
                            var VAPDetails = GetVAPDetails(VAPAmountPerInstallment, _creditLifeDto.application.ApplicationClient.Branch.BranchId, FirstRepayDate, Convert.ToInt32(frequency), Convert.ToInt32(_creditLifeDto.application.Quotation.QuantityInstallments));
                            _creditLifeDto.TotalTAX = VAPDetails.TotalTAX;
                            _creditLifeDto.TotalFuneralSP = VAPDetails.TotalFuneralSP;
                            _creditLifeDto.TotalOthers = VAPDetails.TotalOthers;
                            _creditLifeDto.TAXPerInstallment = VAPDetails.TAXPerInstallment;
                            _creditLifeDto.FuneralPerInstallment = VAPDetails.FuneralPerInstallment;
                            _creditLifeDto.OthersPerInstallment = VAPDetails.OthersPerInstallment;
                            _creditLifeDto.VAP_CST = VAPDetails.VAP_CST;
                            _creditLifeDto.MaxAge = VAPDetails.MaxAge;
                            _creditLifeDto.Age_14_21 = VAPDetails.Age_14_21;
                            _creditLifeDto.Age_06_13 = VAPDetails.Age_06_13;
                            _creditLifeDto.Age_01_05 = VAPDetails.Age_01_05;
                            _creditLifeDto.Age_00_11mt = VAPDetails.Age_00_11mt;
                            _creditLifeDto.VapAmountperInstallment = VAPDetails.VapAmountperInstallment;

                            filterConditions += $"and Application.ApplicationId={_creditLifeDto.application.ApplicationId}";
                            var data = new List<NextOfKinDTO>();
                            data = result.GetNextOfKin(filterConditions, _creditLifeDto.application.ApplicationId).ToList();
                            _creditLifeDto.MainBeneficiary = new NextOfKinDTO();
                            _creditLifeDto.Beneficiaries_Spouse = new NextOfKinDTO();
                            _creditLifeDto.Beneficiaries_Child1 = new NextOfKinDTO();
                            _creditLifeDto.Beneficiaries_Child2 = new NextOfKinDTO();
                            _creditLifeDto.Beneficiaries_Child3 = new NextOfKinDTO();
                            _creditLifeDto.Beneficiaries_Child4 = new NextOfKinDTO();




                            foreach (var item in data)
                            {
                                if (item.BeneficiaryType == 1)
                                {
                                    _creditLifeDto.MainBeneficiary = item;
                                    _creditLifeDto.MainBeneficiary.Age = CalculateAge(item.BirthDate);
                                }
                                else
                                {

                                    if (item.Relation == Atlas.Enumerators.General.RelationType.BeneficiarySpouse)
                                    {
                                        _creditLifeDto.Beneficiaries_Spouse = item;
                                        _creditLifeDto.Beneficiaries_Spouse.Age = CalculateAge(item.BirthDate);
                                    }

                                }
                            }

                            var childList = data.Where(c => c.Relation == Atlas.Enumerators.General.RelationType.BeneficiaryChild).ToList();

                            if (childList.Count() == 1)
                            {
                                _creditLifeDto.Beneficiaries_Child1 = childList[0];
                                _creditLifeDto.Beneficiaries_Child1.Age = CalculateAge(childList[0].BirthDate);
                            }
                            else if (childList.Count() == 2)
                            {
                                _creditLifeDto.Beneficiaries_Child1 = childList[0];
                                _creditLifeDto.Beneficiaries_Child1.Age = CalculateAge(childList[0].BirthDate);
                                _creditLifeDto.Beneficiaries_Child2 = childList[1];
                                _creditLifeDto.Beneficiaries_Child2.Age = CalculateAge(childList[1].BirthDate);
                            }
                            else if (childList.Count() == 3)
                            {
                                _creditLifeDto.Beneficiaries_Child1 = childList[0];
                                _creditLifeDto.Beneficiaries_Child1.Age = CalculateAge(childList[0].BirthDate);

                                _creditLifeDto.Beneficiaries_Child2 = childList[1];
                                _creditLifeDto.Beneficiaries_Child2.Age = CalculateAge(childList[1].BirthDate);

                                _creditLifeDto.Beneficiaries_Child3 = childList[2];
                                _creditLifeDto.Beneficiaries_Child3.Age = CalculateAge(childList[2].BirthDate);
                            }
                            if (childList.Count() == 4)
                            {
                                _creditLifeDto.Beneficiaries_Child1 = childList[0];
                                _creditLifeDto.Beneficiaries_Child1.Age = CalculateAge(childList[0].BirthDate);

                                _creditLifeDto.Beneficiaries_Child2 = childList[1];
                                _creditLifeDto.Beneficiaries_Child2.Age = CalculateAge(childList[1].BirthDate);

                                _creditLifeDto.Beneficiaries_Child3 = childList[2];
                                _creditLifeDto.Beneficiaries_Child3.Age = CalculateAge(childList[2].BirthDate);

                                _creditLifeDto.Beneficiaries_Child4 = childList[3];
                                _creditLifeDto.Beneficiaries_Child4.Age = CalculateAge(childList[3].BirthDate);


                            }


                        }

                        //var temp = _creditLifeDto.MainBeneficiary[0].FirstName;

                    }

                    catch (Exception ex)
                    {
                        log.Info(string.Format("Error getting application {0} details during print quote\nError: {1}", id, ex));
                        throw;
                    }
                }
                if (_creditLifeDto.application != null && _creditLifeDto.application.Quotation != null)
                {
                    try
                    {

                        var result = SnxToPdf(fileName, _creditLifeDto, id, DocCopy);
                        if (otp == true)
                            GenerateOTP(BO_Object.Applications.ToString(), id, BO_ObjectAPI.quotation.ToString().ToLower(), _creditLifeDto.application.Quotation.QuotationId);
                        return result;

                    }
                    catch (Exception ex)
                    {
                        log.Info(string.Format("Error generating PDF file\nError: {0}", id, ex));
                        throw;
                    }
                }
                else
                {
                    log.Info(string.Format("details not found application {0} details while loading NuCard", id));
                    return new HttpResponseMessage(HttpStatusCode.InternalServerError);
                }

            }
            catch (Exception ex)
            {
                log.Error(string.Format("Error generating PDF for application {0}\nError: {1}", id, ex));
                return new HttpResponseMessage(HttpStatusCode.InternalServerError);
            }

        }


        public static HttpResponseMessage SnxToPdf(string fileName, object data, int id, string DocCopy)
        {
            try
            {
                fileName = fileName + ".snx";
                SnapDocumentServer server = new SnapDocumentServer();
                server.LoadDocument(HttpContext.Current.Server.MapPath("~/Integrations/" + fileName));
                server.Document.DataSource = data;

                FileStream stream;
                string targetFolder = "";
                string targetFile = "";
                string path = "";

                if (fileName == "WORDING.snx")
                {
                    targetFolder = "CreditLife";
                    targetFile = "WORDING_";
                    path = getFilePath(targetFolder, targetFile, id, DocCopy);
                    server.ExportDocument(path, SnapDocumentFormat.Pdf);
                    stream = File.OpenRead(path);
                }
                else if (fileName == "ins_oath.snx")
                {
                    targetFolder = "CreditLife";
                    targetFile = "ins_oath_";
                    path = getFilePath(targetFolder, targetFile, id, DocCopy);
                    server.ExportDocument(path, SnapDocumentFormat.Pdf);
                    stream = File.OpenRead(path);
                }
                else if (fileName == "INS_RULE.snx")
                {
                    targetFolder = "CreditLife";
                    targetFile = "INS_RULE_";
                    path = getFilePath(targetFolder, targetFile, id, DocCopy);
                    server.ExportDocument(path, SnapDocumentFormat.Pdf);
                    stream = File.OpenRead(path);
                }
                else if (fileName == "INSURE.snx")
                {
                    targetFolder = "CreditLife";
                    targetFile = "INSURE_";
                    path = getFilePath(targetFolder, targetFile, id, DocCopy);
                    server.ExportDocument(path, SnapDocumentFormat.Pdf);
                    stream = File.OpenRead(path);

                }
                else if (fileName == "mag_oath.snx")
                {
                    targetFolder = "VAP_Contract";
                    targetFile = "mag_oath_";
                    path = getFilePath(targetFolder, targetFile, id, DocCopy);
                    server.ExportDocument(path, SnapDocumentFormat.Pdf);
                    stream = File.OpenRead(path); ;
                }
                else if (fileName == "mag_quot.snx")
                {
                    targetFolder = "VAP_Contract";
                    targetFile = "mag_quot_";
                    path = getFilePath(targetFolder, targetFile, id, DocCopy);
                    server.ExportDocument(path, SnapDocumentFormat.Pdf);
                    stream = File.OpenRead(path);

                }
                else if (fileName == "Mag_Bene.snx")
                {
                    targetFolder = "VAP_Contract";
                    targetFile = "Mag_Bene_";
                    path = getFilePath(targetFolder, targetFile, id, DocCopy);
                    server.ExportDocument(path, SnapDocumentFormat.Pdf);
                    stream = File.OpenRead(path);
                }
                else if (fileName == "mag_poli.snx")
                {
                    targetFolder = "VAP_Contract";
                    targetFile = "mag_poli_";
                    path = getFilePath(targetFolder, targetFile, id, DocCopy);
                    server.ExportDocument(path, SnapDocumentFormat.Pdf);
                    stream = File.OpenRead(path);
                }
                else if (fileName == "mag_word.snx")
                {
                    targetFolder = "VAP_Contract";
                    targetFile = "mag_word_";
                    path = getFilePath(targetFolder, targetFile, id, DocCopy);
                    server.ExportDocument(path, SnapDocumentFormat.Pdf);
                    stream = File.OpenRead(path);
                }
                else if (fileName == "RECEIPT.snx")
                {
                    targetFolder = "Payments/Manual/Receipts";
                    targetFile = "RECEIPT";
                    path = getFilePath(targetFolder, targetFile, id, DocCopy);
                    server.ExportDocument(path, SnapDocumentFormat.Pdf);
                    stream = File.OpenRead(path);
                }
                else if (fileName == "RECEIPT.snx")
                {
                    targetFolder = "Payments/Manual/Receipts";
                    targetFile = "RECEIPT";
                    path = getFilePath(targetFolder, targetFile, id, DocCopy);
                    server.ExportDocument(path, SnapDocumentFormat.Pdf);
                    stream = File.OpenRead(path);
                }
                else
                {
                    throw new Exception($"{fileName} not found");
                }
                var result = FileToHttpResponseMessage(stream, "file.pdf", "application/pdf", id);
                return result;
            }
            catch (Exception ex)
            {
                log.Error(string.Format($"Error in method [FileToHttpResponseMessage] for application {id}, \nError:{ex}"));
                return new HttpResponseMessage(HttpStatusCode.InternalServerError); ;
            }

        }

        public static string getFilePath(string FolderName, String FileName, int appId = 0, string DocCopy = "")
        {
            try
            {
                string path = "";
                log.Info(string.Format($"Getting file '~/Content/{FolderName}/{FileName}.pdf' for application {appId} "));
                if (appId == 0)
                {
                    path = HttpContext.Current.Server.MapPath($"~/Content/{FolderName}/{FileName}.pdf");
                }
                else
                {
                    path = HttpContext.Current.Server.MapPath($"~/Content/{FolderName}/{FileName}{appId}_{DocCopy}.pdf");
                }
                return path;
            }
            catch (Exception ex)
            {
                log.Error(string.Format($"Error in getting FilePath for application {appId}, \nError:{ex}"));
                throw ex;
            }
        }

        public static HttpResponseMessage FileToHttpResponseMessage(FileStream stream, string fileName, string MediaTypeHeaderValue, int appId)
        {
            try
            {
                byte[] fileBytes = new byte[stream.Length];
                String s = Convert.ToBase64String(fileBytes);
                stream.Read(fileBytes, 0, fileBytes.Length);
                stream.Close();
                var result = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(fileBytes)
                };
                result.Content.Headers.ContentDisposition = new System.Net.Http.Headers.ContentDispositionHeaderValue(System.Net.Mime.DispositionTypeNames.Inline)
                {
                    FileName = fileName
                };
                result.Content.Headers.ContentType = new MediaTypeHeaderValue(MediaTypeHeaderValue);
                return result;
            }
            catch (Exception ex)
            {
                log.Error(string.Format($"Error in method [FileToHttpResponseMessage] for application {appId}, \nError:{ex}"));
                throw ex;
            }



        }

        public static string CalculateAge(DateTime Dob)
        {
            try
            {

                DateTime Now = DateTime.Now;
                int _Years = new DateTime(DateTime.Now.Subtract(Dob).Ticks).Year - 1;
                DateTime _DOBDateNow = Dob.AddYears(_Years);
                int _Months = 0;
                for (int i = 1; i <= 12; i++)
                {
                    if (_DOBDateNow.AddMonths(i) == Now)
                    {
                        _Months = i;
                        break;
                    }
                    else if (_DOBDateNow.AddMonths(i) >= Now)
                    {
                        _Months = i - 1;
                        break;
                    }
                }
                int Days = Now.Subtract(_DOBDateNow.AddMonths(_Months)).Days;


                if (_Years <= 0)
                {
                    if (_Months <= 0)
                    {
                        return $"{Days} Days";
                    }
                    return $"{_Months} Months";
                }
                else
                {
                    return $"{_Years} Years";
                }

            }
            catch (Exception ex)
            {
                log.Error(string.Format($"Error in method [FileToHttpResponseMessage], \nError:{ex}"));
                throw ex;
            }

        }


        [HttpGet]
        [Route("applications/fn_printAgree/{id}")]
        public HttpResponseMessage fn_printAgree(int id)
        {
            try
            {
                log.Info(string.Format("Get PDF for application {0}", id));

                // Generating the Customer Acceptance Pdf
                List<string> _docCopy = new List<string>();
                _docCopy.Add("Client");
                _docCopy.Add("Atlas");

                // Generating the Customer Acceptance Pdf
                for (int i = 0; i < _docCopy.Count(); i++)
                {
                    log.Info(String.Format($"[Method:GetMultiplePdf]Generating Quote pdf for application {id}"));
                    GetAgreePdf(id, false, "AGREE.snx", _docCopy[i]);
                    log.Info(String.Format($"[Method:GetMultiplePdf]Generating mag_quot pdf for application {id}"));
                    GetContractPdf(id, "mag_poli", false, _docCopy[i]);

                }
                log.Info(String.Format($"[Method:GetMultiplePdf]Generating INSURE pdf for application {id}"));
                GetContractPdf(id, "ins_oath", false, _docCopy[1]);
                log.Info(String.Format($"[Method:GetMultiplePdf]Generating INSURE pdf for application {id}"));
                GetContractPdf(id, "INS_RULE", false, _docCopy[0]);
                log.Info(String.Format($"[Method:GetMultiplePdf]Generating mag_quot pdf for application {id}"));
                GetContractPdf(id, "mag_word", false, _docCopy[0]);
                log.Info(String.Format($"[Method:GetMultiplePdf]Generating mag_quot pdf for application {id}"));
                GetContractPdf(id, "mag_oath", false, _docCopy[1]);
                log.Info(String.Format($"[Method:GetMultiplePdf]Generating mag_quot pdf for application {id}"));
                GetContractPdf(id, "Mag_Bene", false, _docCopy[1]);
                log.Info(String.Format($"[Method:GetMultiplePdf]Generating INSURE pdf for application {id}"));
                GetContractPdf(id, "WORDING", false, _docCopy[0]); GetPaymentSchedulePdf(id, false, "Payment_Schedule.snx", _docCopy[1]);
                GetPaymentSchedulePdf(id, false, "Payment_Schedule.snx", _docCopy[1]);
                //Get all files from the given folder path.
                string[] path = new string[11];



                path[0] = getFilePath("Quotations", "_Agree", id, _docCopy[0]);
                path[1] = getFilePath("Quotations", "_Agree", id, _docCopy[1]);

                path[2] = getFilePath("VAP_Contract", "mag_poli_", id, _docCopy[0]);
                path[3] = getFilePath("VAP_Contract", "mag_poli_", id, _docCopy[1]);
                path[4] = getFilePath("VAP_Contract", "mag_word_", id, _docCopy[0]);
                path[5] = getFilePath("CreditLife", "ins_rule_", id, _docCopy[0]);
                path[6] = getFilePath("CreditLife", "wording_", id, _docCopy[0]);
                path[7] = getFilePath("VAP_Contract", "mag_oath_", id, _docCopy[1]);
                path[8] = getFilePath("VAP_Contract", "mag_bene_", id, _docCopy[1]);
                path[9] = getFilePath("CreditLife", "ins_oath_", id, _docCopy[1]);
                path[10] = getFilePath("Payment_Schedule", "Payment_Schedule_", id, _docCopy[1]);

                //Merge Multiple Pdf.

                MergeMultiplePDFIntoSinglePDF(getFilePath("MergedAgreementPdf", "_Quota", id), path);

                //Reading file as a stream.
                var stream = File.OpenRead(getFilePath("MergedAgreementPdf", "_Quota", id));


                // Converting Pdf files to the byte array.
                var result = FileToHttpResponseMessage(stream, "file.pdf", "application/pdf", id);

                // Returning file as a Byte Array
                return result;


            }
            catch (Exception ex)
            {
                log.Error(string.Format("Error in Get PDF for application {0}\nError: {1}", id, ex));
                throw; ;
            }

        }


        private string getEditedFields(string editedFields, int applicationId, int newStatus, BO_ObjectAPI type)
        {
            try
            {
                log.Info(string.Format("Get edited fields for client {0}", applicationId));
                using (var result = new BackOfficeWebServiceClient("BackOfficeWebServer.NET"))
                {
                    var currentStatus = result.GetApplicationCategoryStatus(applicationId).Where(o => o.applicationObject == type).FirstOrDefault();
                    if (currentStatus != null)
                    {
                        var fields = new List<VMEditedFields>();
                        if (!string.IsNullOrEmpty(editedFields))
                            fields = JsonConvert.DeserializeObject<List<VMEditedFields>>(editedFields);

                        using (var uow = new UnitOfWork())
                        {
                            string OldStatus = new XPQuery<BOS_Status>(uow).Where(x => x.StatusId == currentStatus.NewStatusId).Select(x => x.Description).FirstOrDefault();
                            string NewStatus = new XPQuery<BOS_Status>(uow).Where(x => x.StatusId == newStatus).Select(x => x.Description).FirstOrDefault();
                            fields.Add(new VMEditedFields() { FieldName = "Status", OldValue = OldStatus, NewValue = NewStatus });
                        }
                        editedFields = JsonConvert.SerializeObject(fields);
                    }
                    return editedFields;
                }
            }
            catch (Exception ex)
            {
                log.Error(string.Format("error getting edited fields for client {0}\nError: {1}", applicationId, ex));
                throw;
            }
        }

        [Route("applications/{id}/gethistory/{subobject}/{version?}")]
        public Response<VMApplicationHistory> GetHistory(int id, string subobject, int version = 0)
        {
            try
            {
                var role = this.HasAccess(BO_Object.Applications, BackOfficeEnum.Action.VIEW);
                if (role.Status == Constants.Success)
                {
                    log.Info(string.Format("Get application history subobject {0}, version {1}", subobject, version));
                    VMApplicationHistory appHistory = new VMApplicationHistory();
                    using (UnitOfWork uow = new UnitOfWork())
                    {
                        appHistory.LatestVersion = new XPQuery<BOS_ApplicationEventHistory>(uow).Where(h => h.ApplicationId == id && h.Category.ToLower() == subobject.ToLower()).Max(x => x.Version);
                        if (version <= 0)
                        {
                            version = appHistory.LatestVersion;
                        }
                        var history = new XPQuery<BOS_ApplicationEventHistory>(uow).FirstOrDefault(h => h.ApplicationId == id && h.Category.ToLower() == subobject.ToLower() && h.Version == version);
                        if (history != null)
                        {
                            Mapper.CreateMap<BOS_ApplicationEventHistory, BOS_ApplicationEventHistoryDTO>();
                            var historyDto = Mapper.Map<BOS_ApplicationEventHistory, BOS_ApplicationEventHistoryDTO>(history);
                            appHistory.History = historyDto;
                            appHistory.CurrentVersion = version;
                            appHistory.OldVersion = version - 1;
                            return Response<VMApplicationHistory>.CreateResponse(Constants.Success, appHistory, null);
                        }
                    }
                    return Response<VMApplicationHistory>.CreateResponse(Constants.Failed, null, new ErrorHandler { ErrorCode = Constants.ApplicationErrorCode, Message = Constants.DetailsNotFoundErrorMessage });
                }
                else
                {
                    log.Info(role.Error);
                    return Response<VMApplicationHistory>.CreateResponse(Constants.Failed, null, role.Error);
                }
            }
            catch (Exception ex)
            {
                log.Error(ex);
                return Response<VMApplicationHistory>.CreateResponse(Constants.Failed, null, new ErrorHandler { ErrorCode = Constants.ApplicationErrorCode, Message = Constants.DetailsNotFoundErrorMessage });
            }

        }


        public Response<bool> fn_CheckDigit(long id)
        {
            bool flag = false;
            var application = GetApplicationById((int)id);

            //uncomment this on local environment
            //using (var result = new BackOfficeWebServiceClient("BackOfficeWebServer.NET"))
            //{
            //    result.UpdateClientCheckRuleStatus((int)id, "CDVCheck");
            //}
            try
            {
                if (application.Data != null)
                {
                    using (var result = new OrchestrationServiceClient("OrchestrationService.NET"))
                    {
                        try
                        {
                            flag = result.PerformCDV(application.Data.ApplicationDetail.BankDetail.Bank.BankId, application.Data.ApplicationDetail.BankDetail.AccountType.AccountTypeId, application.Data.ApplicationDetail.BankDetail.AccountNo, application.Data.ApplicationDetail.BankDetail.Bank.Code);
                        }
                        catch (Exception ex)
                        {
                            log.Error(string.Format("error performing CDV for application {0}\nError: {1}", id, ex));
                            throw;
                        }
                    }
                    if (flag)
                    {
                        try
                        {
                            using (var result = new BackOfficeWebServiceClient("BackOfficeWebServer.NET"))
                            {
                                result.UpdateApplicationCheckRuleStatus((int)id, "CDVCheck", null);
                                result.UpdateClientCheckRuleStatus((int)application.Data.ApplicationDetail.ClientId, "CDVCheck");
                                result.ActivateCustomer((int)application.Data.ApplicationDetail.ClientId, (int)BackOfficeEnum.NewStatus.ACTIVE, "1=1");
                            }
                        }
                        catch (Exception ex)
                        {
                            log.Error(string.Format("error updating application {0} rule status\nError: {1}", id, ex));
                            throw;
                        }
                        log.Info(string.Format("check digit success for application {0}", id));
                        return Response<bool>.CreateResponse(Constants.Success, flag, null);
                    }
                    else
                    {
                        log.Info("execute RejectApplication");
                        string comment = "Application rejected due to incorrect Bank Account details";
                        var result = RejectApplication((int)id, comment, BO_ObjectAPI.bankdetails.ToString());

                        var data = "{\"Comment\": \"" + comment + "\"}";
                        UpdateApplicationEventHistory((int)id, "REJECT_SUBMITTED", data, "", BO_ObjectAPI.bankdetails);

                        log.Info(string.Format("check digit failed for application {0}. Invalid bank details.", id));
                        return Response<bool>.CreateResponse(Constants.Failed, flag, new ErrorHandler() { ErrorCode = Constants.ApplicationErrorCode, Message = Constants.InvalidBankDetails });
                    }
                }
                else
                {
                    log.Info(string.Format("check digit failed for application {0}. Client data null.", id));
                    return Response<bool>.CreateResponse(Constants.Failed, flag, new ErrorHandler() { ErrorCode = Constants.ApplicationErrorCode, Message = Constants.CheckDigitExecutionErrorMessage });
                }
            }
            catch (Exception ex)
            {
                log.Error(string.Format("check digit failed for application {0}\nError: {1}", id, ex));
                return Response<bool>.CreateResponse(Constants.Failed, flag, new ErrorHandler() { ErrorCode = Constants.ApplicationErrorCode, Message = Constants.CheckDigitExecutionErrorMessage });
            }
        }

        public Response<Integrations.AVSResponse> fn_AVSCheckNuPay(int id)
        {
            AVSResponse results = null;
            try
            {
                BackOfficeWebServer.AVSRCheck bankDetail = new BackOfficeWebServer.AVSRCheck();
                var application = GetApplicationById((int)id);
                using (var result = new BackOfficeWebServiceClient("BackOfficeWebServer.NET"))
                {
                    try
                    {
                        ErrorHandler error = new ErrorHandler();
                        bankDetail = result.GetApplicationbankdetails(id, out error);
                    }
                    catch (Exception ex)
                    {
                        log.Error(string.Format("error getting bank details client {0} rule status\nError: {1}", id, ex));
                        throw;
                    }
                }
                if (bankDetail != null)
                {
                    Integrations.AVSRCheck aVSRCheck = new Integrations.AVSRCheck()
                    {
                        card_acceptor = "1227735",
                        user_id = Convert.ToString(ConfigurationManager.AppSettings["userid"]),
                        password = Convert.ToString(ConfigurationManager.AppSettings["password"]),
                        request_id = "0",
                        recieveBank = bankDetail.recieveBank,
                        recieveBranch = bankDetail.recieveBranch,
                        recieveAccno = bankDetail.recieveAccno,
                        accType = "0" + bankDetail.accType,
                        idno = bankDetail.idno,
                        initials = "",
                        name = bankDetail.name,
                        accDebits = "Y",
                        accCredits = "Y",
                        accLenght = "Y"
                    };
                    var serviceResult = new BackOfficeServer.Integrations.Application_Integrations().fn_AVSCheck(aVSRCheck);
                    var json = JsonConvert.SerializeXmlNode(serviceResult);
                    results = JsonConvert.DeserializeObject<Integrations.AVSResponse>(json);

                    if (results.report.ReportError.error_code != "00" || ConfigurationManager.AppSettings["AVSBypass"] == "true")
                    {
                        try
                        {
                            using (var result = new BackOfficeWebServiceClient("BackOfficeWebServer.NET"))
                            {
                                result.UpdateApplicationCheckRuleStatus(id, "AVSCheck", null);
                                result.UpdateClientCheckRuleStatus((int)application.Data.ApplicationDetail.ClientId, "AVSCheck");
                                result.ActivateCustomer((int)application.Data.ApplicationDetail.ClientId, (int)BackOfficeEnum.NewStatus.ACTIVE, "1=1");
                            }
                        }
                        catch (Exception ex)
                        {
                            log.Error(string.Format("error updating application {0} rule status\nError: {1}", id, ex));
                            throw;
                        }
                        log.Info(string.Format("AVS success for application {0}", id));
                        return Response<Integrations.AVSResponse>.CreateResponse(Constants.Success, results, null);
                    }
                    else
                    {
                        log.Info("execute RejectApplication for AVS failure");
                        string comment = "Application rejected due to incorrect Bank Account details";
                        var result = RejectApplication((int)id, comment, BO_ObjectAPI.bankdetails.ToString());

                        var data = "{\"Comment\": \"" + comment + "\"}";
                        UpdateApplicationEventHistory((int)id, "AVS_CHECK", data, "", BO_ObjectAPI.bankdetails);

                        log.Info(string.Format("AVS check failed for application {0}. API failed. Error: {1}", id, results.report.ReportError.error_msg));
                        return Response<Integrations.AVSResponse>.CreateResponse(Constants.Failed, results, new ErrorHandler()
                        {
                            ErrorCode =
                            results.report.ReportError.error_code,
                            Message = results.report.ReportError.error_msg
                        });
                    }
                }
                else
                {
                    log.Info(string.Format("AVS check failed for application {0}. Invalid bank details.", id));
                    return Response<Integrations.AVSResponse>.CreateResponse(Constants.Failed, null, new ErrorHandler() { ErrorCode = Constants.CustomerErrorCode, Message = Constants.InvalidBankDetails });
                }
            }
            catch (Exception ex)
            {
                log.Error(string.Format("check digit failed for application {0}\nError: {1}", id, ex));
                return Response<Integrations.AVSResponse>.CreateResponse(Constants.Failed, null, new ErrorHandler() { ErrorCode = Constants.CustomerErrorCode, Message = Constants.AVSExecutionErrorMessage });
            }
        }

        public async Task<Response<String>> fn_AVSCheckEasyDebit(int id)
        {
            try
            {
                string user = ConfigurationManager.AppSettings["easydebit_user"];
                string password = ConfigurationManager.AppSettings["easydebit_password"];
                string url = ConfigurationManager.AppSettings["easydebit_url"];
                var application = GetApplicationById((int)id);
                BankDetailDto bankDetail = application.Data.ApplicationDetail.BankDetail;
                if (bankDetail != null)
                {
                    log.Info("Generate XML document");
                    StringBuilder xmldoc = new System.Text.StringBuilder();
                    try
                    {
                        xmldoc.Append("<SRQ>");
                        xmldoc.Append("<CR>");
                        xmldoc.Append("<U>" + user + "</U>");
                        xmldoc.Append("<P>" + password + "</P>");
                        xmldoc.Append("</CR>");
                        xmldoc.Append("<RL>");
                        xmldoc.Append("<R>");
                        xmldoc.Append("<CR>" + id + "</CR>");
                        xmldoc.Append("<RT>I</RT>");
                        xmldoc.Append("<AT>S</AT>");
                        xmldoc.Append("<IT>SID</IT>");
                        xmldoc.Append("<IN>" + bankDetail.AccountName.Substring(0, 3) + "</IN>");
                        xmldoc.Append("<N>" + bankDetail.AccountName + "</N>");
                        xmldoc.Append("<ID>" + application.Data.ApplicationDetail.ApplicationClient.IDNumber + "</ID>");
                        xmldoc.Append("<TX></TX>");
                        xmldoc.Append("<BC>" + bankDetail.Bank.Code + "</BC>");
                        xmldoc.Append("<AN>" + bankDetail.AccountNo + "</AN>");
                        xmldoc.Append("<PN></PN>");
                        xmldoc.Append("<EM></EM>");
                        xmldoc.Append("</R>");
                        xmldoc.Append("</RL>");
                        xmldoc.Append("</SRQ>");
                        xmldoc = HandleSpecialCharacters(xmldoc.ToString());
                    }
                    catch (Exception ex)
                    {
                        log.Info(string.Format("error generating XML for score card\nError: {0}", ex));
                        throw;
                    }
                    string res = string.Empty;
                    using (var client = new HttpClient())
                    {
                        StringContent sc = new StringContent(xmldoc.ToString(), Encoding.UTF8, "application/xml");
                        var response = client.PostAsync(url, sc).Result;
                        response.EnsureSuccessStatusCode();
                        var data = await response.Content.ReadAsStringAsync();
                        res = data;
                    }
                    if (res != null && res != "")
                    {
                        XmlDocument doc = new XmlDocument();
                        doc.LoadXml(res);
                        string xacvpath = "ARR/ARL/AR/ACV";
                        var nodes = doc.SelectNodes(xacvpath);
                        string xadpath = "ARR/ARL/AR/AD";
                        var adnodes = doc.SelectNodes(xadpath);
                        string acv = string.Empty;
                        string ad = string.Empty;
                        foreach (XmlNode childrenNode in nodes)
                        {
                            acv = childrenNode.InnerText;
                        }
                        foreach (XmlNode childrenNode in adnodes)
                        {
                            ad = childrenNode.InnerText;
                        }
                        if ((acv == "Y" && ad == "Y") || ConfigurationManager.AppSettings["AVSBypass"] == "true")
                        {
                            try
                            {
                                using (var result = new BackOfficeWebServiceClient("BackOfficeWebServer.NET"))
                                {
                                    result.UpdateApplicationCheckRuleStatus(id, "AVSCheck", null);
                                    result.UpdateClientCheckRuleStatus((int)application.Data.ApplicationDetail.ClientId, "AVSCheck");
                                    result.ActivateCustomer((int)application.Data.ApplicationDetail.ClientId, (int)BackOfficeEnum.NewStatus.ACTIVE, "1=1");
                                }
                            }
                            catch (Exception ex)
                            {
                                log.Error(string.Format("error updating application {0} rule status\nError: {1}", id, ex));
                                throw;
                            }
                            log.Info(string.Format("easydebit success for application {0}", id));
                            return Response<String>.CreateResponse(Constants.Success, "success", null);
                        }
                        else
                        {
                            log.Info("execute RejectApplication for easydebit failure");
                            string comment = "Application rejected due to incorrect Bank Account details";
                            var result = RejectApplication(id, comment, BO_ObjectAPI.bankdetails.ToString());
                            var data = "{\"Comment\": \"" + comment + "\"}";
                            UpdateApplicationEventHistory(id, "AVS_CHECK", data, "", BO_ObjectAPI.bankdetails);
                            log.Info(string.Format("EasyDebit AVS-R failed for application {0}", id));
                            return Response<String>.CreateResponse(Constants.Failed, "fail", new ErrorHandler()
                            {
                            });
                        }
                    }
                    else
                    {
                        log.Info("execute RejectApplication for easydebit failure");
                        string comment = "Application rejected due to incorrect Bank Account details";
                        var result = RejectApplication(id, comment, BO_ObjectAPI.bankdetails.ToString());
                        var data = "{\"Comment\": \"" + comment + "\"}";
                        UpdateApplicationEventHistory(id, "AVS_CHECK", data, "", BO_ObjectAPI.bankdetails);
                        log.Info(string.Format("easydebit  failed for application {0}. API failed. Error", id));
                        return Response<String>.CreateResponse(Constants.Failed, "fail", new ErrorHandler()
                        {
                        });
                    }
                }
                else
                {
                    log.Info(string.Format("easydebit failed for application {0}. Invalid bank details.", id));
                    string comment = "Application rejected due to incorrect Bank Account details";
                    var result = RejectApplication(id, comment, BO_ObjectAPI.bankdetails.ToString());
                    var data = "{\"Comment\": \"" + comment + "\"}";
                    UpdateApplicationEventHistory(id, "AVS_CHECK", data, "", BO_ObjectAPI.bankdetails);
                    return Response<String>.CreateResponse(Constants.Failed, "fail", new ErrorHandler() { ErrorCode = Constants.CustomerErrorCode, Message = Constants.InvalidBankDetails });
                }
            }
            catch (Exception ex)
            {
                log.Error(string.Format("easydebit failed for application {0}\nError: {1}", id, ex));
                string comment = "Application rejected due to incorrect Bank Account details";
                var result = RejectApplication(id, comment, BO_ObjectAPI.bankdetails.ToString());
                var data = "{\"Comment\": \"" + comment + "\"}";
                UpdateApplicationEventHistory(id, "AVS_CHECK", data, "", BO_ObjectAPI.bankdetails);
                return Response<String>.CreateResponse(Constants.Failed, "fail", new ErrorHandler() { ErrorCode = Constants.CustomerErrorCode, Message = Constants.AVSExecutionErrorMessage });
            }
        }

        [HttpPost]
        [Route("applications/AddEditBeneficiary/{ApplicationId}")]
        public Response<List<VMBeneficiaryDetails>> AddEditBeneficiary(List<VMBeneficiaryDetails> obj, int ApplicationId)
        {
            try
            {
                bool flag = true;
                List<VMBeneficiaryDetails> data = null;
                if (obj == null || obj.Count == 0)
                {
                    log.Info(String.Format($"string"));
                    return Response<List<VMBeneficiaryDetails>>.CreateResponse(Constants.Failed + ": [Error ]", obj, null);
                }
                var role = this.HasAccess(BO_Object.Applications, BackOfficeEnum.Action.VIEW);
                if (role.Status == Constants.Success)
                {

                    foreach (var item in obj)
                    {
                        int age = 0;
                        if (item.Relation == Atlas.Enumerators.General.RelationType.BeneficiaryChild)
                        {
                            log.Info(String.Format($"Calculating age for appid{obj[0].ApplicationId}"));
                            var calculatedAge = CalculateAge(item.BirthDate);

                            if (calculatedAge.Contains("Years"))
                            {
                                calculatedAge = calculatedAge.Split(' ')[0];
                                age = Convert.ToInt32(calculatedAge);
                            }

                        }

                        if (item.BeneficiaryType <= 0)
                        {
                            log.Info(string.Format($"BeneficiaryTypeId is less than or equal to 0 for appid:{item.ApplicationId}"));
                            flag = false;
                            break;
                        }
                        else if (string.IsNullOrEmpty(item.Name))
                        {
                            log.Info(string.Format($"Name is null or empty for appid:{item.ApplicationId}"));
                            flag = false;
                            break;
                        }

                        //else if (String.IsNullOrEmpty(item.NationalId))
                        //{
                        //    log.Info(string.Format($"NationalId is null or empty for appid:{item.ApplicationId}"));
                        //    flag = false;
                        //    break;
                        //}
                        else if (item.Relation == Atlas.Enumerators.General.RelationType.NotSet)
                        {
                            log.Info(string.Format($"Relation is not set for appid:{item.ApplicationId}"));
                            flag = false;
                            break;
                        }
                        else if (item.BirthDate == null || item.BirthDate > DateTime.Now)
                        {

                            if (item.BirthDate > DateTime.Now)
                            {
                                log.Info(string.Format($"Future Date Fails for appid:{obj[0].ApplicationId}"));
                                return Response<List<VMBeneficiaryDetails>>.CreateResponse(Constants.Failed, null, new ErrorHandler() { ErrorCode = Constants.CustomerErrorCode, Message = Constants.FutureDateErr });
                            }


                            log.Info(string.Format($"BirthDate is null for appid:{item.ApplicationId}"));
                            flag = false;
                            break;
                        }

                        else if (item.Relation == Atlas.Enumerators.General.RelationType.BeneficiaryChild && age > 21)
                        {
                            log.Info(string.Format($"Child's Age greater than 21 is selected:{obj[0].ApplicationId}"));
                            return Response<List<VMBeneficiaryDetails>>.CreateResponse(Constants.Failed, null, new ErrorHandler() { ErrorCode = Constants.CustomerErrorCode, Message = Constants.ChildAgeLimit });
                        }
                        else if (string.IsNullOrEmpty(item.ContactNo))
                        {
                            log.Info(string.Format($"Contact No. is null for appid:{item.ApplicationId}"));
                            flag = false;
                            break;
                        }
                        else
                        {
                            flag = true;
                        }
                    }
                    if (flag)
                    {
                        using (var result = new BackOfficeWebServiceClient("BackOfficeWebServer.NET"))
                        {
                            data = result.InsertUpdateNextOfKin(obj.ToArray(), ApplicationId).ToList();
                        }
                    }
                    else
                    {
                        log.Info(string.Format($"Validation Fails for appid:{obj[0].ApplicationId}"));
                        return Response<List<VMBeneficiaryDetails>>.CreateResponse(Constants.Failed, null, new ErrorHandler() { ErrorCode = Constants.CustomerErrorCode, Message = Constants.MandatoryBeneficiaryDetails });
                    }
                    return Response<List<VMBeneficiaryDetails>>.CreateResponse(Constants.Success, data, null);
                }
                else
                {

                    log.Info(role.Error);
                    return Response<List<VMBeneficiaryDetails>>.CreateResponse(Constants.Failed, data, role.Error);

                }
            }
            catch (Exception ex)
            {
                log.Error(string.Format("Error in INSERTION/UPDATION in NextOfKin \nError: {0}", ex));
                throw;
            }
        }

        [HttpGet]
        [Route("applications/GetBeneficiaryById/{ApplicationId}")]
        public Response<List<VMBeneficiaryDetails>> GetBeneficiaryById(int ApplicationId = 0)
        {
            try
            {
                var role = this.HasAccess(BO_Object.Applications, BackOfficeEnum.Action.VIEW);
                if (role.Status == Constants.Success)
                {
                    if (ApplicationId > 0)
                    {
                        List<VMBeneficiaryDetails> data = null;
                        using (var result = new BackOfficeWebServiceClient("BackOfficeWebServer.NET"))
                        {
                            var filterConditions = FilterUtil.GetFiltersAsString(BO_Object.Applications, Convert.ToInt32(HttpContext.Current.Session["UserId"]));
                            filterConditions += $" and Application.ApplicationId={ApplicationId} and IdNumber!=null";
                            //filterConditions += $"and NextOfKinId={appid}";

                            data = result.GetNextOfKinByApplication(filterConditions, ApplicationId).ToList();
                        }
                        return Response<List<VMBeneficiaryDetails>>.CreateResponse(Constants.Success, data, null);
                    }
                    else
                    {
                        log.Info("clientid is less than or equal to 0");
                        return Response<List<VMBeneficiaryDetails>>.CreateResponse(Constants.Failed, null, role.Error);
                    }

                }
                else
                {
                    log.Info(role.Error);
                    return Response<List<VMBeneficiaryDetails>>.CreateResponse(Constants.Failed, null, role.Error);
                }
            }
            catch (Exception ex)
            {
                log.Error(string.Format("Error in fetching NextOfKin details  {0}", ex));
                throw;
            }
        }

        [HttpGet]
        [Route("GetVapDetails/{VapAmount}/{branchid}/{loanDate}")]
        public Response<VAP_DESC_ChargesDto> GetCoverageAmount(decimal VapAmount, long branchid, DateTime loanDate)
        {
            using (var result = new BackOfficeWebServiceClient("BackOfficeWebServer.NET"))
            {
                var totalVap = VapAmount;

                if (totalVap <= 0) { return null; }

                int bandID = result.GetVAPDESCBandId(totalVap);
                int vapId = result.GetVAPIdVAPDESCBrandBranch(bandID, (branchid), loanDate);
                var vapDetailsList = result.GetVAPDESCChargeList();

                var vapDetailsRow = vapDetailsList.Where(c => c.Desc_Vap.Desc_VapId == vapId).FirstOrDefault();
                return Response<VAP_DESC_ChargesDto>.CreateResponse(Constants.Success, vapDetailsRow, null);
            }

        }

        public void VerifyEmployer(int applicationId, EmployerDTO employer)
        {
            try
            {
                log.Info(string.Format("verify employer: applicationId: {0}, employerId: {1}", applicationId, employer.EmployerId));
                using (var uow = new UnitOfWork())
                {
                    using (var result = new BackOfficeWebServiceClient("BackOfficeWebServer.NET"))
                    {
                        var filterConditions = FilterUtil.GetFiltersAsString(BO_Object.EmployerDetails, Convert.ToInt32(HttpContext.Current.Session["UserId"]));
                        try
                        {
                            log.Info(string.Format("Updaing employer verification status for application {0}, employerId: {1}", applicationId, employer.EmployerId));
                            //update employer details
                            result.UpdateApplicationCheckRuleStatus(applicationId, "EmployerVerificationCheck", null);

                            ApplicationCaseUtil.UpdateApplicatinCaseState(applicationId, ApplicationCaseObject.EMPLOYER_VERIFY.ToString());
                                                        
                            // To update Verified Employer in FlowFinance

                            JObject payLoad = new JObject(
                                new JProperty("VerifyReason", "Employer Verified")
                            );

                            // var url = "http://localhost:5000/flowfinance/EmployerDetail/Verify/" + ApplicationId;
                            var url = ConfigurationManager.AppSettings["ff_baseurl"] + "EmployerDetail/Verify/" + applicationId;

                            HttpClient httpClient = new HttpClient();
                            httpClient.DefaultRequestHeaders.Add("Authorization", ConfigurationManager.AppSettings["ff_authorization"]);
                            var response = httpClient.PostAsync(url, new StringContent(payLoad.ToString(), Encoding.UTF8, "application/json")).Result;
                        }
                        catch (Exception ex)
                        {
                            log.Error(string.Format("Updaing employer verification status for application {0}, employerId: {1}, error: {2}", applicationId, employer.EmployerId, ex));
                            throw;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                log.Error(string.Format("exception verify employer: applicationId: {0}, employerId: {1}, exception: {2}", applicationId, employer.EmployerId, ex));

            }
        }

        [HttpGet]
        [Route("applications/CheckNuCardStatus/{voucherNumber}")]
        public Response<IssueCardResponse> CheckNuCardStatus(string voucherNumber)
        {

            try
            {
                log.Info(string.Format("Check NuCard Status for voucherNumber {0}", voucherNumber));
                NuCardDetails nuCardDetails = new NuCardDetails()
                {
                    userId = Convert.ToString(ConfigurationManager.AppSettings["userid"]),
                    userPassword = Convert.ToString(ConfigurationManager.AppSettings["password"]),
                    terminalID = Convert.ToString(ConfigurationManager.AppSettings["terminal"]),
                    profileNumber = Convert.ToString(ConfigurationManager.AppSettings["profile"]),
                    voucherNumber = voucherNumber
                };
                var cardStatus = new BackOfficeServer.Integrations.Application_Integrations().NuCardStatus(nuCardDetails);
                if (!string.IsNullOrEmpty(cardStatus.response.errorCode))
                {
                    log.Error(string.Format("Failed NuCardStatus for voucher {0}\nError: {1}", voucherNumber, cardStatus));
                    return Response<IssueCardResponse>.CreateResponse(Constants.Failed, null, new ErrorHandler { ErrorCode = cardStatus.response.errorCode, Message = cardStatus.response.errorMessage });
                }
                log.Info(string.Format("Success NuCard Status for voucherNumber {0}\nResponse: {1}", voucherNumber, cardStatus));
                return Response<IssueCardResponse>.CreateResponse(Constants.Success, cardStatus, null);
            }
            catch (Exception ex)
            {
                log.Error(string.Format("Error in Check NuCard Status for voucher {0}\nError: {1}", voucherNumber, ex));
                return Response<IssueCardResponse>.CreateResponse(Constants.Failed, null, null);
            }
        }

        [HttpGet]
        [Route("applications/GetActiveOTP/{appid}")]
        public Response<OneTimePasswordDto> GetActiveOTP(int appid)
        {
            try
            {
                log.Info(String.Format($"GetActiveOTP method invoked for application: {appid}"));
                OneTimePasswordDto otp = null;
                var filterConditions = FilterUtil.GetFiltersAsString(BO_Object.EmployerDetails, Convert.ToInt32(HttpContext.Current.Session["UserId"]));
                using (var result = new BackOfficeWebServiceClient("BackOfficeWebServer.NET"))
                {
                    filterConditions += $"and objectid={appid} and IsActive=1";
                    otp = result.GetActiveOTP(filterConditions);
                }
                if (otp != null)
                {
                    log.Info(String.Format($"OTP found successfully for application={appid}"));
                    return Response<OneTimePasswordDto>.CreateResponse(Constants.Success, otp, null);
                }
                else
                {
                    log.Info(String.Format($"No Active OTP found for application={appid}"));
                    return Response<OneTimePasswordDto>.CreateResponse(Constants.Failed, null, null);
                }
            }
            catch (Exception ex)
            {
                log.Info(String.Format($"Error in GetActiveOTP for Application={appid}\nError: {ex}"));
                return Response<OneTimePasswordDto>.CreateResponse(Constants.Failed, null, null);
            }
        }

        [HttpGet]
        [Route("applications/ResendOTP/{appid}")]
        public Response<int> ResendOTP(int appid)
        {
            try
            {
                var role = this.HasAccess(BO_Object.Applications, BackOfficeEnum.Action.VIEW);
                if (role.Status == Constants.Success)
                {
                    log.Info(String.Format($"ResendOTP method invoked for application: {appid}"));
                    bool isOTPSuccess = false;
                    OneTimePasswordDto otp = null;
                    OneTimePasswordDto OTPData = new OneTimePasswordDto();
                    var filterConditions = FilterUtil.GetFiltersAsString(BO_Object.EmployerDetails, Convert.ToInt32(HttpContext.Current.Session["UserId"]));
                    using (var result = new BackOfficeWebServiceClient("BackOfficeWebServer.NET"))
                    {
                        filterConditions += $"and objectid={appid} and IsActive=1";
                        log.Info(String.Format($"Invoking GetActiveOTP for application: {appid}"));
                        OTPData = GetActiveOTP(appid).Data;
                        if (OTPData != null)
                        {
                            log.Info(String.Format($"Invoking Otp_Resend for application: {appid}"));
                            isOTPSuccess = result.Otp_Resend(OTPData.OTP, OTPData.ContactNo, OTPData.OTPId);
                        }

                    }
                    if (isOTPSuccess)
                    {
                        log.Info(String.Format($"OTP found successfully for application: {appid}"));
                        return Response<int>.CreateResponse(Constants.Success, OTPData.OTP, null);
                    }
                    else
                    {
                        log.Info(String.Format($"Resend OTP Failed for for application={appid}"));
                        return Response<int>.CreateResponse(Constants.Failed, OTPData.OTP, null);
                    }
                }
                else
                {

                    log.Info(role.Error);
                    return Response<int>.CreateResponse(Constants.Failed, 0, role.Error);

                }
            }
            catch (Exception ex)
            {
                log.Info(String.Format($"Error in ResendOTP for Application={appid}\nError: {ex}"));
                return Response<int>.CreateResponse(Constants.Failed, 0, null);
            }
        }


        [HttpPost]
        [Route("applications/GetEmployerCodes")]
        public Response<List<VWEmployerCodes>> GetEmployerCodes(EmployPara para)
        {
            try
            {
                //data.searchPara = $"'%{data.searchPara.Trim()}%'";
                List<VWEmployerCodes> data = null;
                var filterConditions = FilterUtil.GetFiltersAsString(BO_Object.Applications, Convert.ToInt32(HttpContext.Current.Session["UserId"]));
                using (var result = new BackOfficeWebServiceClient("BackOfficeWebServer.NET"))
                {
                    log.Info(String.Format($"Getting EmployerCodes for Search Term: {para.searchPara}, PageSize: {para.PageSize},PageNo: {para.PageNo} "));
                    data = result.GetEmployerCodes(para.searchPara, para.PageNo, para.PageSize).ToList();
                }
                return Response<List<VWEmployerCodes>>.CreateResponse(Constants.Success, data, null);
            }
            catch (Exception ex)
            {

                log.Error(string.Format($"Error in getting EmployerCodes details \nError: {ex}"));
                return Response<List<VWEmployerCodes>>.CreateResponse(Constants.Failed, null, new ErrorHandler() { ErrorCode = Constants.ApplicationErrorCode, Message = Constants.EmployerCodeNotFound });
            }
        }


        [HttpGet]
        [Route("applications/GetCardAllocationReasons")]
        public Response<List<CardAllocateReasonDto>> GetCardAllocationReasons()
        {
            try
            {
                List<CardAllocateReasonDto> data = null;

                using (var result = new BackOfficeWebServiceClient("BackOfficeWebServer.NET"))
                {
                    log.Info("Getting card allocation reasons");
                    data = result.GetCardAllocationReasons()?.ToList();
                    if (data != null && data.Count() > 0)
                        return Response<List<CardAllocateReasonDto>>.CreateResponse(Constants.Success, data, null);
                }
                return Response<List<CardAllocateReasonDto>>.CreateResponse(Constants.Failed, data, new ErrorHandler() { ErrorCode = Constants.ApplicationErrorCode, Message = Constants.CardAllocationReasonsNotFound });
            }
            catch (Exception ex)
            {

                log.Error($"error fetching card allocate reasons.\nError: {ex}");
                return Response<List<CardAllocateReasonDto>>.CreateResponse(Constants.Failed, null, new ErrorHandler() { ErrorCode = Constants.ApplicationErrorCode, Message = Constants.CardAllocationReasonsNotFound });
            }
        }

        [HttpGet]
        [Route("applications/GetCardAllocationFees")]
        public Response<List<CardAllocationFeesDto>> GetCardAllocationFees()
        {
            try
            {
                List<CardAllocationFeesDto> data = null;

                using (var result = new BackOfficeWebServiceClient("BackOfficeWebServer.NET"))
                {
                    log.Info("Getting card allocation fees");
                    data = result.GetCardAllocationFees()?.ToList();
                    if (data != null && data.Count() > 0)
                        return Response<List<CardAllocationFeesDto>>.CreateResponse(Constants.Success, data, null);
                }
                return Response<List<CardAllocationFeesDto>>.CreateResponse(Constants.Failed, null, new ErrorHandler() { ErrorCode = Constants.ApplicationErrorCode, Message = Constants.CardAllocationFeesNotFound });

            }
            catch (Exception ex)
            {

                log.Error($"error fetching card allocate fees.\nError: {ex}");
                return Response<List<CardAllocationFeesDto>>.CreateResponse(Constants.Failed, null, new ErrorHandler() { ErrorCode = Constants.ApplicationErrorCode, Message = Constants.CardAllocationFeesNotFound });
            }
        }

        [HttpGet]
        [Route("applications/CheckCardAlreadyAllocated/{clientId}")]
        public bool CheckToAllocateNewCard(int clientId, string idNumber)
        {
            try
            {
                log.Info(string.Format("Get customer {0} applications", clientId));
                bool allocateNewCard = false;
                List<VMClientApplication> clientApplications = new List<VMClientApplication>();
                using (var client = new BackOfficeWebServiceClient("BackOfficeWebServer.NET"))
                {
                    clientApplications = client.GetApplicationsByClientId(clientId)?.ToList();
                    if (clientApplications != null)
                    {
                        log.Info($"{clientApplications.Count()} Client applications found in BO");
                        if (clientApplications.Count() > 1)
                        {
                            using (UnitOfWork uow = new UnitOfWork())
                            {
                                int clientAccounts = 0;
                                log.Info($"get client's {clientId} accounts");
                                foreach (var application in clientApplications)
                                {
                                    if (clientAccounts == 0)
                                    {
                                        var account = new XPQuery<ACC_Account>(uow).Where(a => a.AccountNo == application.AccountNo && a.Status.StatusId >= 40)?.FirstOrDefault();
                                        if (account != null)
                                            clientAccounts++;
                                    }
                                }
                                if (clientAccounts == 0)
                                {
                                    log.Info($"Accounts not found for client {clientId} in BO. Checking loans in ASS.");
                                    allocateNewCard = true;
                                }
                            }
                        }
                        else
                            allocateNewCard = true;
                    }
                    else
                    {
                        log.Info($"applications not found for client {clientId}");
                        allocateNewCard = false;
                    }

                    if (allocateNewCard)
                    {
                        //check loans in ASS
                        log.Info($"Checking client {clientId} loans in ASS");
                        //**************************************************************
                        //int count = DBManager.GetLoanCountFromASS(idNumber);
                        //allocateNewCard = count > 0 ? false : true;
                        allocateNewCard = true;
                    }
                }
                log.Info($"Allocate new card flag: {allocateNewCard}");
                return allocateNewCard;
            }
            catch (Exception ex)
            {
                log.Error(string.Format("Error in getting customer {0} applications\nError: {1}", clientId, ex));
                throw;
            }
        }

        [HttpGet]
        [Route("applications/CalculateNuCardFees/{cardType}")]
        public decimal CalculateNuCardFees(BOS_NuCardType cardType)
        {
            try
            {
                using (var result = new BackOfficeWebServiceClient("BackOfficeWebServer.NET"))
                {
                    log.Info($"Getting card fees for card type {cardType.ToString()}");
                    var cardFeesWithoutVAT = result.GetCardAllocationFees()?.Where(c => c.CardType.ToLower() == cardType.ToString().ToLower())?.FirstOrDefault();
                    if (cardFeesWithoutVAT != null)
                    {
                        if (cardFeesWithoutVAT.Fees > 0)
                        {
                            var VATPercent = 15;
                            var VAT = cardFeesWithoutVAT.Fees * VATPercent / 100;
                            var totalCardFees = cardFeesWithoutVAT.Fees + VAT;

                            return Math.Round(Convert.ToDecimal(totalCardFees), 2, MidpointRounding.AwayFromZero);
                        }
                        else
                            log.Info($"fees for card type {cardType.ToString()} is 0");
                    }
                    else
                    {
                        log.Fatal($"fees not found for card type {cardType.ToString()}");
                        throw new Exception($"fees not found for card type {cardType.ToString()}");
                    }
                    return 0.00M;
                }
            }
            catch (Exception ex)
            {
                log.Error(string.Format("Error in getting fees for card type: {0} \nError: {1}", cardType, ex));
                throw;
            }
        }

        [NonAction]
        public Response<CardSwipe.AuthRsp> CreateNewNuCardDebitOrder(int id, Atlas.Domain.Structures.AccountInfo accountInfo, decimal cardFees)
        {
            try
            {
                log.Info(string.Format("Create New NuCard debit order for application {0}", id));
                var applicationDto = new ApplicationDto();
                using (var uow = new UnitOfWork())
                {
                    using (var result = new BackOfficeWebServiceClient("BackOfficeWebServer.NET"))
                    {
                        try
                        {
                            var appFilterConditions = FilterUtil.GetFiltersAsString(BO_Object.Applications, Convert.ToInt32(HttpContext.Current.Session["UserId"]));
                            applicationDto = result.GetApplicationDetail(appFilterConditions, id);
                        }
                        catch (Exception ex)
                        {
                            log.Info(string.Format("Error getting application {0} details while creating debit order\nError: {1}", id, ex));
                            throw;
                        }
                    }
                    if (applicationDto != null)
                    {
                        try
                        {
                            {
                                try
                                {
                                    var terminal = new XPQuery<TCC_Branch>(uow).Where(x => x.TerminalId == applicationDto.Disbursement.TCCTerminalID).FirstOrDefault();
                                    CardSwipe.AuthRsp serviceResult = new CardSwipe.AuthRsp();
                                    Atlas.Domain.DTO.Account.ACC_DebitOrderDTO dbtOrder = new Atlas.Domain.DTO.Account.ACC_DebitOrderDTO();
                                    string branchCode = new XPQuery<BRN_Branch>(uow).Where(x => x.BranchId == applicationDto.ApplicationClient.Branch.BranchId).FirstOrDefault().LegacyBranchNum;
                                    string Contractno = CommonHelper.GetValidContractNumber(branchCode, Convert.ToString(applicationDto?.ClientId), Convert.ToString(applicationDto?.AccountId), false, true);
                                    if (ConfigurationManager.AppSettings["tccbypass"] == "false")
                                    {
                                        CardDebitOrder cardDebitOrder = new CardDebitOrder()
                                        {
                                            Contract_no = Contractno,   //branchCode + "x" + applicationDto.ApplicationClient.ApplicationClientId.ToString() + "x" + applicationDto.ApplicationId.ToString() + "xN",

                                            install_amnt = Convert.ToInt32(cardFees * 100).ToString(),
                                            contract_amnt = Convert.ToInt32((cardFees) * 100).ToString(),
                                            installments = "1",
                                            frequency = new Atlas.ThirdParty.NuPay.Util.TCCHelper().GetTCCFrequencyValue(applicationDto.Employer.SalaryType.ToString()),
                                            start_date = (Convert.ToDateTime(applicationDto.Quotation.FirstRepayDate)).ToString("yyyyMMdd"),
                                            employer = applicationDto.Employer.EmployerCode?.ToString(),
                                            adj_rule = ConfigurationManager.AppSettings["adj_rule"],
                                            tracking = ConfigurationManager.AppSettings["tracking"],
                                            client_ID = applicationDto.ApplicationClient.IDNumber,//accountInfo.AccountId.ToString(),
                                            customScreen = true,
                                            panIn = "",
                                            accountNumberIn = (applicationDto.BankDetail == null ? "" : applicationDto.BankDetail.AccountNo),
                                            aedoGlobalTimeout = Convert.ToInt32(ConfigurationManager.AppSettings["aedoGlobalTimeout"]),
                                            Line1 = (applicationDto.ResidentialAddress == null ? "" : applicationDto.ResidentialAddress.AddressLine1),
                                            Line2 = (applicationDto.ResidentialAddress == null ? "" : applicationDto.ResidentialAddress.AddressLine2),
                                            Line3 = (applicationDto.ResidentialAddress == null ? "" : applicationDto.ResidentialAddress.AddressLine3),
                                            Line4 = (applicationDto.ResidentialAddress == null ? "" : applicationDto.ResidentialAddress.AddressLine4 == null ? "" : applicationDto.ResidentialAddress.AddressLine4),
                                            Merchant_ID = terminal.MerchantId,
                                            Term_ID = terminal.TerminalId
                                        };
                                        try
                                        {
                                            log.Info("[CreateNewNuCardDebitOrder]: cardDebitOrder Requeset : " + new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(cardDebitOrder));

                                            serviceResult = new Application_Integrations().fn_CreateDebitOrder(cardDebitOrder);

                                            var response = CommonHelper.ErrorHandlingforSwipeCard(serviceResult, applicationDto.AccountId);
                                            if (response != null && response.Status == Constants.Failed.ToString())
                                            {
                                                return response;
                                            }

                                            log.Info("Debit order generated for accountId : " + applicationDto.AccountId.ToString());
                                        }
                                        catch (Exception ex)
                                        {
                                            log.Error(string.Format("Exception occured while debit order generation for accountId : {0} and \nException : {1}", accountInfo.AccountId.ToString(), ex.ToString()));
                                            serviceResult = new CardSwipe.AuthRsp();
                                            throw;
                                        }

                                        dbtOrder = new Atlas.Domain.DTO.Account.ACC_DebitOrderDTO()
                                        {
                                            AccountId = Convert.ToInt64(accountInfo.AccountId),
                                            accountNumber = serviceResult.AccountNumber,
                                            accountType = serviceResult.AccountType,
                                            adjRule = serviceResult.AdjRule,
                                            approvalCode = serviceResult.ApprovalCode,
                                            contractAmount = (serviceResult.ContractAmount == null ? 0 : Convert.ToDecimal(serviceResult.ContractAmount)),
                                            frequency = serviceResult.Frequency,
                                            pAN = serviceResult.PAN,
                                            responseCode = serviceResult.ResponseCode,
                                            tracking = serviceResult.Tracking,
                                            transactionID = string.IsNullOrEmpty(serviceResult.TransactionID) ? "0" : serviceResult.TransactionID,
                                            ContractNumber = cardDebitOrder.Contract_no
                                        };
                                    }
                                    else
                                    {
                                        log.Info("Mock tccbypass is true. Mocking data.\nBy New NuCard Debit Pass Debit order generated for accountId : " + applicationDto.AccountId.ToString());
                                        branchCode = new XPQuery<BRN_Branch>(uow).Where(x => x.BranchId == applicationDto.ApplicationClient.Branch.BranchId).FirstOrDefault().LegacyBranchNum;
                                        CardDebitOrder cardDebitOrder = new CardDebitOrder()
                                        {
                                            Contract_no = CommonHelper.GetValidContractNumber(branchCode, Convert.ToString(applicationDto?.ClientId), Convert.ToString(applicationDto?.AccountId), false, true),//branchCode + applicationDto.ApplicationClient.ApplicationClientId.ToString() + applicationDto.ApplicationId.ToString() + "xN",
                                            install_amnt = "1",
                                            contract_amnt = "1",
                                            installments = "1",
                                            frequency = applicationDto.Employer.SalaryType.ToString(),
                                            start_date = DateTime.Now.AddDays(10).ToString("yyyyMMdd"),
                                            employer = "01",
                                            adj_rule = "01",
                                            tracking = "14",
                                            client_ID = applicationDto.ApplicationClient.IDNumber.ToString(),
                                            customScreen = true,
                                            panIn = "",
                                            accountNumberIn = (applicationDto.BankDetail == null ? "" : applicationDto.BankDetail.AccountNo),
                                            aedoGlobalTimeout = Convert.ToInt32(ConfigurationManager.AppSettings["aedoGlobalTimeout"]),//sec
                                            Line1 = (applicationDto.ResidentialAddress == null ? "" : applicationDto.ResidentialAddress.AddressLine1),
                                            Line2 = (applicationDto.ResidentialAddress == null ? "" : applicationDto.ResidentialAddress.AddressLine2),
                                            Line3 = (applicationDto.ResidentialAddress == null ? "" : applicationDto.ResidentialAddress.AddressLine3),
                                            Line4 = (applicationDto.ResidentialAddress == null ? "" : applicationDto.ResidentialAddress.AddressLine4 == null ? "" : applicationDto.ResidentialAddress.AddressLine4),
                                            Merchant_ID = terminal.MerchantId,
                                            Term_ID = terminal.TerminalId,
                                        };
                                        try
                                        {
                                            log.Info("[CreateNewNuCardDebitOrder]: cardDebitOrder TestData Request : " + new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(cardDebitOrder));
                                            serviceResult = new Application_Integrations().fn_CreateDebitOrder(cardDebitOrder);

                                            //var response = CommonHelper.ErrorHandlingforSwipeCard(serviceResult, applicationDto.AccountId);
                                            //if (response != null && response.Status == Constants.Failed.ToString())
                                            //{
                                            //    return response;
                                            //}

                                            log.Info("Debit order generated for accountId : " + applicationDto.AccountId.ToString());
                                        }
                                        catch (Exception ex)
                                        {
                                            log.Error(string.Format("Exception occured while debit order generation for accountId : {0} and \nException : {1}", accountInfo.AccountId.ToString(), ex.ToString()));
                                            serviceResult = new CardSwipe.AuthRsp();
                                            throw;
                                        }

                                        serviceResult.ResponseCode = "00";
                                        dbtOrder = new Atlas.Domain.DTO.Account.ACC_DebitOrderDTO()
                                        {
                                            AccountId = Convert.ToInt64(accountInfo.AccountId),
                                            accountNumber = "0000004064271453",
                                            accountType = "Cheque",
                                            adjRule = "Move Fwd",
                                            approvalCode = "",
                                            contractAmount = cardFees,
                                            frequency = applicationDto.Employer.SalaryType.ToString(),
                                            pAN = "4451470012438140",
                                            responseCode = "00-Approved or completed successfully",
                                            tracking = "3 days",
                                            transactionID = "0153700636",
                                            ContractNumber = cardDebitOrder.Contract_no
                                        };
                                    }

                                    SaveDebitOrderResponse(dbtOrder);
                                    log.Info("[CreateNewNuCardDebitOrder]: Response : " + new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(serviceResult));
                                    if (serviceResult.ResponseCode.Contains("00"))
                                        return Response<CardSwipe.AuthRsp>.CreateResponse(Constants.Success, serviceResult, null);
                                    else
                                        return Response<CardSwipe.AuthRsp>.CreateResponse(Constants.Failed, null, new ErrorHandler() { ErrorCode = serviceResult.ResponseCode, Message = EnumCheck.Description((Enum_AEDO_Auth_Req)Convert.ToInt32(serviceResult.ResponseCode)) });
                                }
                                catch (Exception ex)
                                {
                                    log.Error(string.Format("Swipe card error while creating debit order\nError: {0}", ex));
                                    throw;
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            log.Error(string.Format("Error getting account details while creating debit order\nError: {0}", ex));
                            throw;
                        }
                    }
                    else
                    {
                        log.Info("Debit order creation failed.");
                        return Response<CardSwipe.AuthRsp>.CreateResponse(Constants.Failed, null, new ErrorHandler() { ErrorCode = Constants.ApplicationErrorCode, Message = Constants.ApplicationDetailsNotFound });
                    }

                }
            }
            catch (Exception ex)
            {
                log.Info(ex.ToString());
                return Response<CardSwipe.AuthRsp>.CreateResponse(Constants.Failed, null, new ErrorHandler() { ErrorCode = Constants.ApplicationErrorCode, Message = Constants.DebitOrderCreationErrorMessage });
            }
        }

        [NonAction]
        private Atlas.Domain.Structures.AccountInfo CreateNuCardAccount(int applicationId, decimal fees)
        {
            using (var uow = new UnitOfWork())
            {
                using (var result = new BackOfficeWebServiceClient("BackOfficeWebServer.NET"))
                {
                    log.Info($"create nucard account for application {applicationId}");
                    try
                    {
                        //get consultant person id
                        var staffPersonId = Convert.ToInt64(HttpContext.Current.Session["PersonId"]);

                        //get application details
                        var appFilterConditions = FilterUtil.GetFiltersAsString(BO_Object.Applications, Convert.ToInt32(HttpContext.Current.Session["UserId"]));
                        var application = result.GetApplicationDetail(appFilterConditions, applicationId);

                        //get loan account
                        var account = new XPQuery<ACC_Account>(uow).FirstOrDefault(x => x.AccountNo == application.AccountNo);

                        if (account == null)
                        {
                            log.Fatal($"Account details not found for application {applicationId}");
                            throw new Exception($"Account details not found for application {applicationId}");
                        }

                        //create nucard account
                        var data = result.CreateNuCardAccount(applicationId, fees, account.Period, Atlas.Enumerators.Account.PeriodFrequency.Monthly, staffPersonId);
                        if (data != null)
                        {
                            if (data.AccountId > 0)
                            {
                                log.Info(String.Format($"[CreateNuCardAccount] invoking InsertUpdate_BOS_NuCardClientMap for application Id :{applicationId}"));
                                InsertUpdate_BOS_NuCardClientMap(applicationId);
                            }
                        }
                        return data;
                    }
                    catch (Exception ex)
                    {
                        log.Error($"Error creating nucard account for application {applicationId}\nException {ex}");
                        throw;
                    }
                }
            }
        }

        [NonAction]
        public Response<string> AllocateNewCard(int applicationId, int reasonId = 1)
        {
            try
            {
                //get application
                var application = GetApplicationById(applicationId).Data;

                //calculate new card fees
                var type = (BOS_NuCardType)reasonId;
                var cardFees = CalculateNuCardFees(type);

                //create new account for new card
                var accountInfo = CreateNuCardAccount(applicationId, cardFees);

                //create debit order for new card fees if fees > 0
                if (cardFees > 0)
                {
                    var debitOrderResponse = CreateNewNuCardDebitOrder(applicationId, accountInfo, cardFees);
                    if (debitOrderResponse.Status == Constants.Failed)
                        return Response<string>.CreateResponse(Constants.Failed, null, new ErrorHandler()
                        {
                            ErrorCode = debitOrderResponse.Error.ErrorCode,
                            Message = debitOrderResponse.Error.Message
                        });
                }

                //if replacement card then get balance, transfer funds, block old card, update nucard status
                if (application.ApplicationDetail.Disbursement.CardDetails?.NewVocherNumber != null)// && debitOrderResponse.Status == Constants.Success)
                {
                    var balanceResponse = fn_BalanceNuCard(applicationId, application.ApplicationDetail.Disbursement.CardDetails.OldVocherNumber);
                    if (balanceResponse.Status == Constants.Success)
                    {
                        var jsonObj = balanceResponse.Data;
                        var bal = jsonObj.methodResponse.@params.param.value.@struct.member.Where(a => a.name == "balanceAmount").Select(a => a.value.@int).FirstOrDefault();

                        var transferResponse = TransferFunds_TF(applicationId, application.ApplicationDetail.Disbursement.CardDetails.OldVocherNumber, application.ApplicationDetail.Disbursement.CardDetails.NewVocherNumber, bal);

                        if (transferResponse.Status == Constants.Success)
                        {
                            BlockCard card = new BlockCard()
                            {
                                ReasonId = application.ApplicationDetail.Disbursement.CardDetails.ReasonId,
                                VoucherNumber = application.ApplicationDetail.Disbursement.CardDetails.OldVocherNumber
                            };
                            var blockResponse = BlockCard(card);
                            if (blockResponse.Status == Constants.Success)
                            {
                                using (var res = new BackOfficeWebServiceClient("BackOfficeWebServer.NET"))
                                {
                                    res.UpdateDisbursementVoucher(application.ApplicationDetail.Disbursement.DisbursementId);
                                }

                                using (UnitOfWork uow = new UnitOfWork())
                                {
                                    var nucardId = new XPQuery<NUC_NuCard>(uow).FirstOrDefault(d => d.SequenceNum == application.ApplicationDetail.Disbursement.CardDetails.NewVocherNumber)?.NuCardId;
                                    var oldNucardId = new XPQuery<NUC_NuCard>(uow).FirstOrDefault(n => n.SequenceNum == application.ApplicationDetail.Disbursement.CardDetails.OldVocherNumber)?.NuCardId;
                                    if (nucardId != null && nucardId > 0 && oldNucardId != null && oldNucardId > 0)
                                    {
                                        NuCardDataController ctrl = new NuCardDataController();
                                        ctrl.UpdateNuCardStatus((int)nucardId, (int)Atlas.Enumerators.NuCard.NuCardStatus.ISSUE);
                                        ctrl.UpdateNuCardStatus((int)oldNucardId, (int)Atlas.Enumerators.NuCard.NuCardStatus.BLOCKED);

                                        //close nucard account against this voucher
                                        var accountId = new XPQuery<BOS_NuCardClientMap>(uow).FirstOrDefault(n => n.NuCardId == oldNucardId)?.AccountId;
                                        if (accountId != null && accountId > 0)
                                        {
                                            var account = new XPQuery<ACC_Account>(uow).FirstOrDefault(a => a.AccountId == accountId);
                                            account.Status = new XPQuery<ACC_Status>(uow).Where(s => s.StatusId == (int)NewStatus.STATUS_CLOSED).FirstOrDefault();
                                            account.NewStatusId = new XPQuery<ACC_Status>(uow).Where(s => s.StatusId == (int)NewStatus.STATUS_CLOSED).FirstOrDefault().StatusId;
                                            account.CloseDate = DateTime.Now;
                                            uow.Save(account);
                                            uow.CommitChanges();
                                        }
                                    }
                                }

                                return Response<string>.CreateResponse(Constants.Success, "New card allocated", null);
                            }
                        }
                        return Response<string>.CreateResponse(Constants.Failed, null, new ErrorHandler()
                        {
                            ErrorCode = Constants.NuCardError,
                            Message = Constants.FundTransferDetails
                        });
                    }
                    return Response<string>.CreateResponse(Constants.Failed, null, new ErrorHandler()
                    {
                        ErrorCode = Constants.NuCardError,
                        Message = Constants.BalanceNuCardMessage
                    });
                }

                //else if (debitOrderResponse.Status == Constants.Success)
                {
                    using (UnitOfWork uow = new UnitOfWork())
                    {
                        var nucardId = new XPQuery<NUC_NuCard>(uow).FirstOrDefault(d => d.SequenceNum == application.ApplicationDetail.Disbursement.NuCardNumber)?.NuCardId;
                        if (nucardId != null)
                        {
                            NuCardDataController ctrl = new NuCardDataController();
                            ctrl.UpdateNuCardStatus((int)nucardId, (int)Atlas.Enumerators.NuCard.NuCardStatus.ISSUE);
                        }
                    }
                    return Response<string>.CreateResponse(Constants.Success, "New card allocated", null);
                }
            }
            catch (Exception ex)
            {
                log.Error($"Error in allocating new card for application {applicationId}\nError: {ex}");
                return Response<string>.CreateResponse(Constants.Failed, null, new ErrorHandler() { ErrorCode = Constants.ApplicationErrorCode, Message = Constants.DebitOrderCreationErrorMessage });
            }
        }

        [HttpGet]
        [Route("applications/ContractCancellation_CC/{transactionID}")]
        public Response<String> fn_ContractCancellation_CC(int transactionID)
        {
            try
            {
                log.Info($"[fn_ContractCancellation_CC] invoked for TransactionId :{transactionID}");
                Atlas.ThirdParty.NuPay.Models.DebitOrderCancellationDetails CancelDebitOrderObj = new Atlas.ThirdParty.NuPay.Models.DebitOrderCancellationDetails
                {
                    Username = ConfigurationManager.AppSettings["uName"].ToString(),
                    Password = ConfigurationManager.AppSettings["uPass"].ToString(),
                    LendorType = ConfigurationManager.AppSettings["uMerchantType"].ToString(),
                    LendorID = ConfigurationManager.AppSettings["uMerchant"].ToString(),
                    TransactionID = transactionID
                };
                log.Info($"[fn_ContractCancellation_CC] request for TransactionId :{transactionID}");
                var result = new Atlas.ThirdParty.NuPay.Integrations.NuCardIntegration().ContractCancellation_CC(CancelDebitOrderObj);
                log.Info($"[fn_ContractCancellation_CC] response for TransactionId :{transactionID}, \nResponse :{result}");
                if (result != null && result == "00")
                {
                    log.Info($"[fn_ContractCancellation_CC] Response Code:{result}, Response Success for TransactionId :{transactionID}");
                    return Response<String>.CreateResponse(Constants.Success, result, null);
                }
                else
                {
                    log.Info($"[fn_ContractCancellation_CC] Response Code:{result}, Response failed for TransactionId :{transactionID}");
                    return Response<String>.CreateResponse(Constants.Failed, result, null);
                }
            }
            catch (Exception ex)
            {
                log.Error($"Error in ContractCancellation_CC for TransactionId :{transactionID}\nError: {ex}");
                return Response<String>.CreateResponse(Constants.Failed, null, new ErrorHandler() { ErrorCode = Constants.ApplicationErrorCode, Message = ex.Message.ToString() });
            }
        }


        [HttpGet]
        [Route("applications/InstalmentCancellation_IC/{transactionID}/{instalment}")]
        public Response<String> fn_InstalmentCancellation_IC(int transactionID, int instalment)
        {
            try
            {
                log.Info($"[fn_InstalmentCancellation_IC] invoked for TransactionId :{transactionID}");
                Atlas.ThirdParty.NuPay.Models.DebitOrderCancellationDetails CancelDebitOrderObj = new Atlas.ThirdParty.NuPay.Models.DebitOrderCancellationDetails
                {
                    Username = ConfigurationManager.AppSettings["uName"].ToString(),
                    Password = ConfigurationManager.AppSettings["uPass"].ToString(),
                    LendorType = ConfigurationManager.AppSettings["uMerchantType"].ToString(),
                    LendorID = ConfigurationManager.AppSettings["uMerchant"].ToString(),
                    TransactionID = transactionID,
                    Instalment = instalment
                };
                log.Info($"[fn_InstalmentCancellation_IC] request for TransactionId :{transactionID}");
                var result = new Atlas.ThirdParty.NuPay.Integrations.NuCardIntegration().InstalmentCancellation_IC(CancelDebitOrderObj);
                log.Info($"[fn_InstalmentCancellation_IC] response for TransactionId :{transactionID}, \nResponse :{result}");
                if (result != null && result == "0")
                {
                    log.Info($"[fn_InstalmentCancellation_IC] Response Code:{result}, Response Success for TransactionId :{transactionID}");
                    return Response<String>.CreateResponse(Constants.Success, result, null);
                }
                else
                {
                    log.Info($"[fn_InstalmentCancellation_IC] Response Code:{result}, Response failed for TransactionId :{transactionID}");
                    return Response<String>.CreateResponse(Constants.Failed, result, null);
                }
            }
            catch (Exception ex)
            {
                log.Error($"Error in InstalmentCancellation_IC for TransactionId :{transactionID}\nError: {ex}");
                return Response<String>.CreateResponse(Constants.Failed, null, new ErrorHandler() { ErrorCode = Constants.ApplicationErrorCode, Message = ex.Message.ToString() });
            }
        }

        [HttpGet]
        [Route("Applications/TransferFunds_TF/{appid}/{voucherNumber}/{voucherNumberTo}/{Amount}")]
        public Response<Integrations.RootObject> TransferFunds_TF(int appid, string voucherNumber, string voucherNumberTo, string Amount)
        {
            try
            {
                string userId = string.Empty;
                string userPassword = string.Empty;
                string terminalID = string.Empty;
                string profileNumber = string.Empty;
                using (var uow = new UnitOfWork())
                {
                    var nucardConfig = new XPQuery<ThirdParty_Config>(uow).Where(config => config.ThirdParty.ThirdPartyId == (int)BackOfficeEnum.ThirdPartyIntegration.NuCard && config.Branch.BranchId == Convert.ToInt64(HttpContext.Current.Session["BranchId"])).FirstOrDefault();

                    if (nucardConfig != null)
                    {
                        userId = nucardConfig.UserId;
                        userPassword = nucardConfig.Password;
                        terminalID = nucardConfig.AppId;
                        profileNumber = nucardConfig.ProfileId;
                    }
                }
                log.Info(string.Format($"[TransferFunds_TF] load nuCard invoked for appid: {appid}"));
                Atlas.ThirdParty.NuPay.Models.NuCardDetails loadNuCard = new Atlas.ThirdParty.NuPay.Models.NuCardDetails()
                {
                    userId = userId,
                    userPassword = userPassword,
                    terminalID = terminalID,
                    profileNumber = profileNumber,
                    voucherNumber = voucherNumber,
                    VoucherNumberTo = voucherNumberTo,
                    requestAmount = Amount
                };
                log.Info(string.Format($"[TransferFunds_TF] Request: TransferFunds_TF for appid: {appid}"));
                var serviceResult = new Atlas.ThirdParty.NuPay.Integrations.NuCardIntegration().TransferFunds_TF(loadNuCard);
                log.Info(string.Format($"[TransferFunds_TF] Response: {serviceResult} for appid: {appid}"));
                var json = JsonConvert.SerializeXmlNode(serviceResult);
                var results = JsonConvert.DeserializeObject<IssueCardResponse>(json);
                if (results.response.tutukaResponse != null)
                {
                    try
                    {
                        results.response.tutukaResponse = results.response.tutukaResponse.Replace("<?xml version=\"1.0\" encoding=\"UTF-8\"?>", "");
                        System.Xml.XmlDocument doc = new System.Xml.XmlDocument(); doc.LoadXml(results.response.tutukaResponse);
                        var jsonObj = JsonConvert.DeserializeObject<Integrations.RootObject>(JsonConvert.SerializeXmlNode(doc));
                        log.Info(string.Format($"[TransferFunds_TF] Response: Success for appid: {appid}"));
                        return Response<Integrations.RootObject>.CreateResponse(Constants.Success, jsonObj, null);
                    }
                    catch (Exception ex)
                    {
                        log.Error(string.Format("[TransferFunds_TF]Error parsing tutukaResponse. \nError: {0}", ex));
                        throw;
                    }
                }
                else
                {
                    log.Info(string.Format("service error while executing TransferFunds_TF. Error {0}", results.response.errorMessage));
                    return Response<Integrations.RootObject>.CreateResponse(Constants.Failed, null, new ErrorHandler() { ErrorCode = results.response.errorCode, Message = results.response.errorMessage });
                }
            }
            catch (Exception ex)
            {
                log.Info(string.Format("Error executing TransferFunds_TF.\nError: {0}", ex));
                throw;
            }


        }


        private void InsertUpdate_BOS_NuCardClientMap(int appId, Int64 nuCardStatus = 1)
        {
            try
            {
                log.Info(String.Format($"[InsertUpdate_BOS_NuCardClientMap] invoked for ApplicationId :{appId}"));
                ApplicationDto application = null;
                using (var result = new BackOfficeWebServiceClient("BackOfficeWebServer.NET"))
                {
                    var filterConditions = FilterUtil.GetFiltersAsString(BO_Object.Applications, Convert.ToInt32(HttpContext.Current.Session["UserId"]));
                    log.Info(String.Format($"[InsertUpdate_BOS_NuCardClientMap] Request: Application Details for ApplicationId :{appId}"));
                    application = result.GetApplicationDetail(filterConditions, appId);
                    log.Info(String.Format($"[InsertUpdate_BOS_NuCardClientMap] Response: Application Details for ApplicationId :{appId}"));
                }
                BOS_NuCardClientMap nuCardClientMapObj = new BOS_NuCardClientMap();
                using (var uow = new UnitOfWork())
                {

                    log.Info(String.Format($"[InsertUpdate_BOS_NuCardClientMap] Request: BOS_NuCardClientMap Details for ApplicationId :{appId}"));
                    var data = new XPQuery<BOS_NuCardClientMap>(uow)?
                        .FirstOrDefault(a => a.ApplicationId == appId);

                    log.Info(String.Format($"[InsertUpdate_BOS_NuCardClientMap] Request: NUC_NuCard Details against NuCardNumber :{application.Disbursement.NuCardNumber} for ApplicationId :{appId}"));

                    long nuCardId = 0;
                    //replacement card - existing client
                    if (application.Disbursement.CardDetails != null && !string.IsNullOrEmpty(application.Disbursement.CardDetails.NewVocherNumber))
                    {
                        nuCardId = new XPQuery<NUC_NuCard>(uow)
                            .FirstOrDefault(nuCard => nuCard.SequenceNum == application.Disbursement.CardDetails.NewVocherNumber)
                            .NuCardId;
                    }
                    //new card - fresh client
                    else
                    {
                        nuCardId = new XPQuery<NUC_NuCard>(uow)
                            .FirstOrDefault(nuCard => nuCard.SequenceNum == application.Disbursement.NuCardNumber)
                            .NuCardId;
                    }

                    var status = new NUC_NuCardStatus();

                    ////Entry in Inventory
                    //Int64 TrackingNum = Convert.ToInt64(new XPQuery<NUC_NuCard>(uow).Max(a => a.TrackingNum)) + 1;

                    //if (nuCardId == 0 || nuCardId == null)
                    //{
                    //    log.Info(String.Format($"[InsertUpdate_BOS_NuCardClientMap] Inserting Sequence Number = NuCardNumber :{application.Disbursement.NuCardNumber} in NUC_NuCard for ApplicationId :{appId}"));
                    //    NUC_NuCard nuCardObj = new NUC_NuCard(uow);

                    //    nuCardObj.SequenceNum = application.Disbursement.NuCardNumber;
                    //    nuCardObj.TrackingNum = Convert.ToString(TrackingNum);
                    //    nuCardObj.Status = new XPQuery<NUC_NuCardStatus>(uow).FirstOrDefault(st => st.NuCardStatusId == nuCardStatus);
                    //    nuCardObj.Save();
                    //    uow.CommitChanges();

                    //    log.Info(String.Format($"[InsertUpdate_BOS_NuCardClientMap] Request: NUC_NuCard Details against NuCardNumber :{application.Disbursement.NuCardNumber} for ApplicationId :{appId}"));

                    //    nuCardId = new XPQuery<NUC_NuCard>(uow)
                    //       .Where(nuCard => nuCard.SequenceNum == application.Disbursement.NuCardNumber)
                    //       .Select(nuCard => nuCard.NuCardId)
                    //       .FirstOrDefault();
                    //}


                    if (data == null)
                    {
                        //Insert into BOS_NuCardClientMapDTO
                        if (nuCardId != 0)
                        {


                            log.Info(String.Format($"[InsertUpdate_BOS_NuCardClientMap] inserting data into BOS_NuCardClientMapDTO for ApplicationId :{appId}"));
                            var getMaxId = new XPQuery<BOS_NuCardClientMap>(uow).
                            Max(a => a.NuCardClientMapId);
                            nuCardClientMapObj.NuCardClientMapId = ++getMaxId;
                            nuCardClientMapObj.NuCardId = Convert.ToInt64(nuCardId);
                            nuCardClientMapObj.AccountId = Convert.ToInt64(application.LoanAccount.FirstOrDefault(a => a.Application.ApplicationId == application.ApplicationId && a.AccountType.Description.ToUpper() == "NUCARD").AccountId);
                            nuCardClientMapObj.ApplicationId = application.ApplicationId;
                            nuCardClientMapObj.ClientId = Convert.ToInt64(application.ClientId);
                            nuCardClientMapObj.NationalId = application.ApplicationClient?.IDNumber;
                            nuCardClientMapObj.AllocationDate = DateTime.UtcNow;
                            nuCardClientMapObj.AccountCardFirstName = application.ApplicationClient?.Firstname;
                            nuCardClientMapObj.AccountCardSurname = application.ApplicationClient?.Surname;
                            nuCardClientMapObj.AccountCardCellphone = application.ApplicationClient?.CellNo;
                            nuCardClientMapObj.Save();
                        }
                    }
                    else
                    {
                        //Update table BOS_NuCardClientMapDTO     
                        log.Info(String.Format($"[InsertUpdate_BOS_NuCardClientMap] Updating data into BOS_NuCardClientMapDTO for ApplicationId :{appId}"));
                        data.NuCardId = Convert.ToInt64(nuCardId);
                        data.AccountId = Convert.ToInt64(application.LoanAccount.FirstOrDefault(a => a.Application.ApplicationId == application.ApplicationId && a.AccountType.Description.ToUpper() == "NUCARD").AccountId);
                        data.ApplicationId = application.ApplicationId;
                        data.ClientId = Convert.ToInt64(application.ClientId);

                        data.NationalId = application.ApplicationClient.IDNumber;
                        data.AllocationDate = DateTime.UtcNow;
                        data.AccountCardFirstName = application.ApplicationClient.Firstname;
                        data.AccountCardSurname = application.ApplicationClient.Surname;
                        data.AccountCardCellphone = application.ApplicationClient.CellNo;
                        data.Save();
                    }
                    uow.CommitChanges();
                    log.Info(String.Format($"[InsertUpdate_BOS_NuCardClientMap] data Inserted/Updated into BOS_NuCardClientMapDTO for ApplicationId :{appId}"));
                }
            }
            catch (Exception ex)
            {
                log.Error(String.Format($"Exception in [InsertUpdate_BOS_NuCardClientMap] for ApplicationId: {appId} \n Error :{ex}"));
                throw ex;
            }
        }

        private void UpdateNuCardClientMapping(int applicationId)
        {
            try
            {
                log.Info(String.Format($"[UpdateNuCardClientMapping] invoked for ApplicationId :{applicationId}"));

                using (var uow = new UnitOfWork())
                {
                    var app = GetApplicationById(applicationId)?.Data;
                    if (app != null)
                    {
                        log.Info($"application found for appId {applicationId}");
                        var nucardId = new XPQuery<NUC_NuCard>(uow).FirstOrDefault(n => n.SequenceNum == app.ApplicationDetail.Disbursement.NuCardNumber)?.NuCardId;
                        if (nucardId != null && nucardId > 0)
                        {
                            log.Info($"nucardId: {nucardId} found for appId {applicationId}");
                            var mapping = new XPQuery<BOS_NuCardClientMap>(uow).Where(n => n.NuCardId == nucardId)?.FirstOrDefault();

                            if (mapping != null)
                            {
                                log.Info($"mapping found for nationalId {nucardId}");
                                mapping.ApplicationId = applicationId;
                                mapping.AccountId = (long)app.ApplicationDetail.AccountId;
                                mapping.ClientId = (int)app.ApplicationDetail.ClientId;
                                mapping.Save();
                                uow.CommitChanges();
                                log.Info(String.Format($"[UpdateNuCardClientMapping] data updated for ApplicationId :{applicationId}"));
                            }
                            else
                            {
                                log.Info($"mapping not found for nationalId {nucardId}");
                            }
                        }
                        else
                        {
                            log.Info($"nationalId not found for appId {applicationId}");
                        }
                    }
                    else
                    {
                        log.Info($"appId not found for appId {applicationId}");
                    }
                }
            }
            catch (Exception ex)
            {
                log.Error(String.Format($"Exception in [UpdateNuCardClientMapping] for ApplicationId: {applicationId} \n Error :{ex}"));
                throw ex;
            }
        }


        public Response<Integrations.RootObject> BlockCard(BlockCard blockCard)
        {
            try
            {
                var role = this.HasAccess(BO_Object.NuCard, BackOfficeEnum.Action.NUCARD_BLOCK_CARD);
                if (role.Status == Constants.Success)
                {
                    string userId = string.Empty;
                    string userPassword = string.Empty;
                    string terminalID = string.Empty;
                    string profileNumber = string.Empty;
                    using (var uow = new UnitOfWork())
                    {
                        var nucardConfig = new XPQuery<ThirdParty_Config>(uow).Where(config => config.ThirdParty.ThirdPartyId == (int)BackOfficeEnum.ThirdPartyIntegration.NuCard && config.Branch.BranchId == Convert.ToInt64(HttpContext.Current.Session["BranchId"])).FirstOrDefault();

                        if (nucardConfig != null)
                        {
                            userId = nucardConfig.UserId;
                            userPassword = nucardConfig.Password;
                            terminalID = nucardConfig.AppId;
                            profileNumber = nucardConfig.ProfileId;
                        }
                    }
                    //return GetNuCardDetailsById(Convert.ToInt32(nuCardId));
                    log.Info($"[BlockCard] block nuCard invoked for voucher: {blockCard.VoucherNumber}, reasonId: {blockCard.ReasonId}");
                    Atlas.ThirdParty.NuPay.Models.NuCardDetails nuCardDetails = new Atlas.ThirdParty.NuPay.Models.NuCardDetails()
                    {
                        userId = userId,
                        userPassword = userPassword,
                        terminalID = terminalID,
                        profileNumber = profileNumber,
                        voucherNumber = blockCard.VoucherNumber,
                        ReasonId = blockCard.ReasonId.ToString()
                    };
                    log.Info(string.Format($"[BlockCard] Request: TransferFunds_TF for voucher: {blockCard.VoucherNumber}, reasonId {blockCard.ReasonId}"));
                    var serviceResult = new Atlas.ThirdParty.NuPay.Integrations.NuCardIntegration().StopCard(nuCardDetails);
                    log.Info(string.Format($"[BlockCard] for appid: {blockCard.VoucherNumber} Response: {serviceResult}"));
                    var json = JsonConvert.SerializeXmlNode(serviceResult);
                    var results = JsonConvert.DeserializeObject<Atlas.ThirdParty.NuPay.Models.IssueCardResponse>(json);
                    if (results.response.tutukaResponse != null)
                    {
                        try
                        {
                            results.response.tutukaResponse = results.response.tutukaResponse.Replace("<?xml version=\"1.0\" encoding=\"UTF-8\"?>", "");
                            System.Xml.XmlDocument doc = new System.Xml.XmlDocument(); doc.LoadXml(results.response.tutukaResponse);
                            var jsonObj = JsonConvert.DeserializeObject<Integrations.RootObject>(JsonConvert.SerializeXmlNode(doc));
                            log.Info(string.Format($"[BlockCard] Response: Success for voucher: {blockCard.VoucherNumber}"));
                            return Response<Integrations.RootObject>.CreateResponse(Constants.Success, jsonObj, null);

                        }
                        catch (Exception ex)
                        {
                            log.Error(string.Format("[BlockCard]Error parsing tutukaResponse. \nError: {0}", ex));
                            throw;
                        }
                    }
                    else
                    {
                        log.Info(string.Format("service error while executing TransferFunds_TF. Error {0}", results.response.errorMessage));
                        return Response<Integrations.RootObject>.CreateResponse(Constants.Failed, null, new ErrorHandler() { ErrorCode = results.response.errorCode, Message = results.response.errorMessage });
                    }
                }
                else
                    return Response<Integrations.RootObject>.CreateResponse(Constants.Failed, null, new ErrorHandler() { ErrorCode = Constants.NuCardError, Message = Constants.AuthorizationFailed });
            }
            catch (Exception ex)
            {
                log.Info(string.Format("Error executing TransferFunds_TF.\nError: {0}", ex));
                throw;
            }
        }
        private DisbursementDto UpdateTrackingNum(string nuCardNumber, int disbursementId)
        {
            try
            {
                log.Info(string.Format($"[UpdateTrackingNum] invoked for nuCardNumber : {nuCardNumber}"));
                DisbursementDto res = null;
                using (var uow = new UnitOfWork())
                {
                    log.Info(string.Format($"[UpdateTrackingNum] Fetching data from NUC_NuCard against nuCardNumber : {nuCardNumber}"));
                    var data = new XPQuery<NUC_NuCard>(uow)?.Where(nuCard => nuCard.SequenceNum == nuCardNumber)?.FirstOrDefault();
                    Mapper.CreateMap<NUC_NuCard, Atlas.Domain.DTO.Nucard.NUC_NuCardDTO>();
                    if (data != null)
                    {
                        log.Info(string.Format($"[UpdateTrackingNum] Mapping data from NUC_NuCard to NUC_NuCardDTO for nuCardNumber : {nuCardNumber}"));
                        var nuCardDetails = Mapper.Map<NUC_NuCard, Atlas.Domain.DTO.Nucard.NUC_NuCardDTO>(data);

                        using (var result = new BackOfficeWebServiceClient("BackOfficeWebServer.NET"))
                        {
                            log.Info(string.Format($"[UpdateTrackingNum] invoking [UpdateTrackingNumber] : {nuCardNumber}"));
                            res = result.UpdateTrackingNumber(nuCardDetails, disbursementId);
                            //res.nuCardBalance = NucardBalance(res.NuCardNumber);
                        }
                    }



                    log.Info(string.Format($"[UpdateTrackingNum] Returning Result for nuCardNumber : {nuCardNumber}"));
                    return res;
                }
            }
            catch (Exception ex)
            {
                log.Error(string.Format($"Exception in [UpdateTrackingNum] for NuCardNumber: {nuCardNumber} \n Error :{ex}"));
                throw ex;
            }
        }

        [NonAction]
        private HttpResponseMessage GetPaymentSchedulePdf(int id, bool otp = false, string fileName = "Payment_Schedule.snx", string DocCopy = "Client")
        {
            try
            {
                log.Info("[GetPaymentSchedulePdf] function started.");
                PaymentScheduleDto paymentScheduleDTO = new PaymentScheduleDto();
                var paymentSchedules = new List<PaymentSchedules>();
                var account = new ACC_Account();
                ApplicationDto application = new ApplicationDto();
                using (var result = new BackOfficeWebServiceClient("BackOfficeWebServer.NET"))
                {
                    var filterConditions = FilterUtil.GetFiltersAsString(BO_Object.Applications, Convert.ToInt32(HttpContext.Current.Session["UserId"]));
                    application = result.GetApplicationDetail(filterConditions, id);

                    if (application != null && application.ApplicationClient != null && application.ApplicationClient.Branch != null && application.ApplicationClient.Branch.BranchId != null && application.ApplicationClient.Branch.BranchId != 0)
                    {
                        paymentScheduleDTO.Name = application.ApplicationClient.Firstname + " " + application.ApplicationClient.Surname;
                        paymentScheduleDTO.IDNumber = application.ApplicationClient.IDNumber;

                        if (application.Quotation != null)
                        {
                            paymentScheduleDTO.Term = application.Quotation.QuantityInstallments.ToString();
                            paymentScheduleDTO.Interest = application.Quotation.InterestRate;
                        }

                        using (var uoW = new UnitOfWork())
                        {
                            var branchDetails = new XPQuery<BRN_Branch>(uoW).Where(x => x.BranchId == application.ApplicationClient.Branch.BranchId).FirstOrDefault();
                            if (branchDetails != null)
                            {
                                paymentScheduleDTO.BranchName = branchDetails.LegacyBranchNum + " " + branchDetails.BranchName;
                            }

                            account = new XPQuery<ACC_Account>(uoW).Where(acc => acc.AccountId == application.AccountId).FirstOrDefault();
                            var schedules = new XPQuery<ACC_Schedules>(uoW).Where(acc_sch => acc_sch.AccountId == application.AccountId).FirstOrDefault();

                            if (account != null)
                            {
                                paymentScheduleDTO.LoanAmount = account.LoanAmount;
                                var debitOrder = new XPQuery<ACC_DebitOrder>(uoW).Where(debit => debit.AccountId == account.AccountId).FirstOrDefault();
                                if (debitOrder != null)
                                {
                                    paymentScheduleDTO.Contract = debitOrder.ContractNumber;
                                }
                            }
                            if (schedules != null)
                            {
                                paymentScheduleDTO.LoanInstalment = (schedules.Installment.HasValue ? schedules.Installment.Value : 0.0M)
                                                                + (schedules.Premium.HasValue ? schedules.Premium.Value : 0.0M)
                                                                + (schedules.Servicefee.HasValue ? schedules.Servicefee.Value : 0.0M);
                                paymentScheduleDTO.VapInstalment = schedules.VAP.HasValue ? schedules.VAP.Value : 0.0M;
                            }

                            if (application.Employer != null)
                            {
                                string frequency = string.Empty;
                                if (account.PeriodFrequency != null)
                                {
                                    if (application.Employer.AddressTypeId.AddressTypeId == (int)SalaryFrequency.Monthly)
                                    {
                                        frequency = "M";
                                        paymentScheduleDTO.PayDate = frequency + " " + application.Employer.PayDay;
                                    }
                                    if (application.Employer.AddressTypeId.AddressTypeId == (int)SalaryFrequency.Weekly)
                                    {
                                        frequency = "W";
                                        paymentScheduleDTO.PayDate = frequency + " " + Enum.GetName(typeof(DayOfWeek), application.Employer.PayDay);
                                    }
                                    if (application.Employer.AddressTypeId.AddressTypeId == (int)SalaryFrequency.Fortnightly)
                                    {
                                        frequency = "B";
                                        paymentScheduleDTO.PayDate = frequency + " " + application.Employer.PayDay.ToString();
                                    }
                                }
                                paymentScheduleDTO.Term = frequency + " " + paymentScheduleDTO.Term;
                            }

                            if (application.ApplicationClient.CountryOfBirthId.HasValue || application.ApplicationClient.CountryOfBirthId > 0)
                            {
                                var countryList = result.GetCountry();
                                if (countryList != null)
                                {
                                    var country = countryList.Where(c => c.CountryId == application.ApplicationClient.CountryOfBirthId).FirstOrDefault();
                                    if (country != null)
                                    {
                                        paymentScheduleDTO.CountryCode = country.Code;
                                    }
                                }
                            }
                        }

                        if (application.BankDetail != null && application.BankDetail.Bank != null && application.BankDetail.AccountType != null)
                        {
                            paymentScheduleDTO.BankAccount = application.BankDetail.Bank.BankNo + " "
                                                             + application.BankDetail.AccountNo + " "
                                                             + application.BankDetail.Bank.Code + " "
                                                             + application.BankDetail.AccountType.Description;
                        }
                    }
                }

                paymentScheduleDTO.Today = DateTime.Now.ToString("dd/MM/yy");
                paymentScheduleDTO.DocCopy = DocCopy;

                paymentSchedules = DBManager.GetSchedulesForPaymentReport(Convert.ToInt64(account.AccountId));
                if (paymentSchedules != null && paymentSchedules.Count > 0)
                {
                    paymentScheduleDTO.Total = paymentScheduleDTO.LoanInstalment + paymentScheduleDTO.VapInstalment;
                }
                SnapDocumentServer server = new SnapDocumentServer();
                server.LoadDocument(HttpContext.Current.Server.MapPath("~/Integrations/" + fileName));
                server.Document.DataSource = paymentScheduleDTO;
                server.Document.DataSources.Add(new DataSourceInfo("PaymentSchedules", paymentSchedules.OrderBy(x => x.No).ToList()));

                FileStream stream;
                server.ExportDocument(HttpContext.Current.Server.MapPath("~/Content/Payment_Schedule/Payment_Schedule_" + id + "_" + DocCopy + ".pdf"), SnapDocumentFormat.Pdf);
                stream = File.OpenRead(HttpContext.Current.Server.MapPath("~/Content/Payment_Schedule/Payment_Schedule_" + id + "_" + DocCopy + ".pdf"));


                byte[] fileBytes = new byte[stream.Length];
                String s = Convert.ToBase64String(fileBytes);
                stream.Read(fileBytes, 0, fileBytes.Length);
                stream.Close();
                var response = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(fileBytes)
                };
                response.Content.Headers.ContentDisposition = new System.Net.Http.Headers.ContentDispositionHeaderValue(System.Net.Mime.DispositionTypeNames.Inline)
                {
                    FileName = "file.pdf"
                };
                response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
                return response;
            }
            catch (Exception ex)
            {
                log.Info("Error in [GetPaymentSchedulePdf] function : " + ex);
                return null;
            }
            finally
            {
                log.Info("[GetPaymentSchedulePdf] function end.");
            }
        }

        public async Task<Response<string>> SendQuote(int ApplicationId)
        {
            //  Generte PDF
            GetMultiplePdf(ApplicationId);

            //  FTP Quote to FF
            try
            {
                SendFileToServer.SendFile(
        HttpContext.Current.Server.MapPath("~/Content/CustomerAcceptanceContracts/_Quota" + ApplicationId + "_Client.pdf"),
        "/CustomerAcceptanceContracts/");
            }
            catch (Exception e)
            {
                log.Error(e);
            }

            ApplicationDto application = new ApplicationDto();
            using (var uow = new UnitOfWork())
            {
                var app = GetApplicationById(ApplicationId)?.Data;
                if (app != null)
                {
                    log.Info($"application found for appId {ApplicationId}");
                    int QuotationId = app.ApplicationDetail.Quotation.QuotationId;

                    // var url = "http://localhost:5000/flowfinance/Quotation/receiveQuote/" + ApplicationId + "/" + QuotationId;
                    var url = ConfigurationManager.AppSettings["ff_baseurl"] + "Quotation/receiveQuote/" + ApplicationId + "/" + QuotationId;

                    var filePath = @"C:\ALMS\atlas-online\AtlasDev\AtlasBackOffice\BackOfficeService\Content\Quotations\_Quota20720_Atlas.pdf";

                    HttpClient httpClient = new HttpClient();
                    MultipartFormDataContent form = new MultipartFormDataContent();

                    FileStream fs = File.OpenRead(filePath);
                    var streamContent = new StreamContent(fs);

                    var imageContent = new ByteArrayContent(streamContent.ReadAsByteArrayAsync().Result);
                    imageContent.Headers.ContentType = MediaTypeHeaderValue.Parse("multipart/form-data");

                    form.Add(imageContent, "pdf", Path.GetFileName(filePath));
                    httpClient.DefaultRequestHeaders.Add("Authorization", ConfigurationManager.AppSettings["ff_authorization"]);
                    var response = httpClient.PostAsync(url, form).Result;

                    //int QuotationId = app.ApplicationDetail.Quotation.QuotationId;
                    //var filename = "_Quota20720_Atlas.pdf";
                    //var filePath = "C:\\ALMS\\atlas-online\\AtlasDev\\AtlasBackOffice\\BackOfficeService\\Content\\Quotations\\" + filename.ToString();
                    //byte[] by = File.ReadAllBytes(filePath);
                    //var url = "http://localhost:5000/flowfinance/Quotation/receiveQuote/" + ApplicationId + "/" + QuotationId;
                    //using (var client = new HttpClient())
                    //{
                    //    using (var content =
                    //        new MultipartFormDataContent("Upload----" + DateTime.Now.ToString(CultureInfo.InvariantCulture)))
                    //    {
                    //        content.Add(new StreamContent(new MemoryStream(by)), filename, filename);
                    //        using (
                    //           var message =
                    //               await client.PostAsync(url, content))
                    //        {
                    //            var input = await message.Content.ReadAsStringAsync();

                    //        }
                    //    }
                    //}
                }

            }


            return null;
        }

        public void RejectQuotation() { }

        public void VerifyDocument(int applicationId, DocumentsDto document)
        {
            try
            {
                log.Info(string.Format("verify document: applicationId: {0}", applicationId));
                using (var uow = new UnitOfWork())
                {
                    using (var result = new BackOfficeWebServiceClient("BackOfficeWebServer.NET"))
                    {
                        var filterConditions = FilterUtil.GetFiltersAsString(BO_Object.Documents, Convert.ToInt32(HttpContext.Current.Session["UserId"]));
                        try
                        {
                            log.Info(string.Format("Updaing document verification status for application {0}", applicationId));

                            result.UpdateApplicationCheckRuleStatus(applicationId, "DocumentsVerificationCheck", null);

                            ApplicationCaseUtil.UpdateApplicatinCaseState(applicationId, ApplicationCaseObject.DOCUMENT_VERIFY.ToString());
                            
                            // To update Verified Documents in FlowFinance
                            JObject payLoad = new JObject(
                                new JProperty("VerifyDocument", "Documents Verified")
                            );
                            //var url = "http://localhost:5000/flowfinance/Document/VerifyDocuments/" + ApplicationId;
                            var url = ConfigurationManager.AppSettings["ff_baseurl"] + "Document/VerifyDocuments/" + applicationId;

                            HttpClient httpClient = new HttpClient();
                            httpClient.DefaultRequestHeaders.Add("Authorization", ConfigurationManager.AppSettings["ff_authorization"]);
                            var response = httpClient.PostAsync(url, new StringContent(payLoad.ToString(), Encoding.UTF8, "application/json")).Result;
                        }
                        catch (Exception ex)
                        {
                            log.Error(string.Format("Updaing document verification status for application {0}, error: {1}", applicationId, ex));
                            throw;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                log.Error(string.Format("exception verify document: applicationId: {0}, exception: {1}", applicationId, ex));
            }
        }

        public Response<string> fn_DisburseWithABSA(int pApplicationId)
        {
            try
            {
                log.Info("Disbursement with ABSA started for applicationId : " + pApplicationId);

                // ABSAInsert(pApplicationId);
                // CreateABSATransmissionFile(pApplicationId);
                log.Info("Disbursement with ABSA end for applicationId : " + pApplicationId);
                return Response<string>.CreateResponse(Constants.Success, null, null);
            }
            catch(Exception ex)
            {
                log.Info(ex);
                return Response<string>.CreateResponse(Constants.Failed, null, null);
            }
        }


        public Response<string> UpdateFlowFinanceDisbursement(int ApplicationId, string AlmsAccountNo)
        {
            JObject payLoad = new JObject(
                new JProperty("ApplicationId", AlmsAccountNo)
            );

            //var url = "http://localhost:5000/flowfinance/Quotation/" + ApplicationId + "/disburse/" + AlmsAccountNo + "/";
            var url = ConfigurationManager.AppSettings["ff_baseurl"] + "Quotation/" + ApplicationId + "/disburse/" + AlmsAccountNo + "/";
            
            HttpClient httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Add("Authorization", ConfigurationManager.AppSettings["ff_authorization"]);
            var response = httpClient.PostAsync(url, new StringContent(payLoad.ToString(), Encoding.UTF8, "application/json")).Result;

            log.Info("Disburse");
            return Response<string>.CreateResponse(Constants.Success, null, null);
        }
        [HttpPost]
        [Route("Applications/PrepareMandate")]
        public void PrepareMandate(){
            Condition condition = null;
            List<VMApplication> applications = DBManager.GetApplicationView("ApplicationsForDebicheckCreate", 1, ref condition);

            foreach (VMApplication application in applications){
                try
                {
                    var res = fn_CreateDebicheckDebitOrder(application.ApplicationId);
                    using (var result = new BackOfficeWebServiceClient("BackOfficeWebServer.NET"))
                    {
                        var filterConditions = "1=1";
                        var StatusResult = result.UpdateApplicationDisbursementStatus(filterConditions, application.ApplicationId, (int)BackOfficeEnum.NewStatus.STATUS_DEBIT_ORDER_CREATED);
                    }
                    DBManager.UpdateApplicationCMSState(application.ApplicationId, BackOfficeEnum.Action.APPLICATION_CREATE_DEBIT_ORDER.ToString());
                }
                catch (Exception ex) {
                    log.Error(string.Format("PrepareMandate faild for application id :", application.ApplicationId));
                    log.Error(ex.Data);
                }

                //  Mock  Authorization
                //if (Boolean.Parse(ConfigurationManager.AppSettings["isTestEnvironment"]))
                if (Boolean.Parse(ConfigurationManager.AppSettings["MockDebiCheck"]))
                {
                    log.Info("Mock : Debicheck Debit Order");
                    ApproveDebicheckDebitOrder(application.ApplicationId);
                }
            }
        }

        [HttpPost]
        [Route("Applications/ApproveMandateAsync")]
        public async void ApproveMandateAsync(){
           List<String> date= DBManager.GetMinAnDMaxDate();

            if (date[0].Length == 0 || date[1].Length == 0)
            {
                log.Info("No Mandates waiting for Approval");
                return;
            }

            DateTime startDate =  DateTime.ParseExact(date[1], "dd-MM-yyyy HH:mm:ss",
                System.Globalization.CultureInfo.InvariantCulture);
            DateTime endDate = DateTime.ParseExact(date[0], "dd-MM-yyyy HH:mm:ss",
               System.Globalization.CultureInfo.InvariantCulture);
            DebiCheckIntegration ob = new DebiCheckIntegration();
            ob.GetTokenAsync();
            MandateInitiateReport res=await ob.GetMandateInitiate(startDate, endDate);

            if(res == null || res.MandateInitiateReportGetResponse.Count == 0)
            {
                HttpContext.Current.Response.StatusCode = 200;
                return;
            }

            foreach (MandateInitiateReportGetResponse re in res.MandateInitiateReportGetResponse)
            {               
                try
                {
                    using (var uow = new UnitOfWork())
                    {
                        var debiCheckMandate = new XPQuery<DebiCheckMandate>(uow).Where(x => x.ContractNumber == re.ContractNumber && x.Status== "CREATED").FirstOrDefault();
                        if (debiCheckMandate != null)
                        {
                            ApproveDebicheckDebitOrder(debiCheckMandate.ApplicationId);
                        }
                    }
                }
                catch (Exception ex)
                {
                    log.Error(string.Format("ApproveMandateAsync faild for application id :", re.ContractNumber));
                    log.Error(ex.Data);
                }

            }

        }
        [HttpGet]
        [Route("Applications/ApproveMandateAsync/{ApplicationId}")]
        public void MandateApprovalMock(int ApplicationId)
        {
            try
            {
                using (var uow = new UnitOfWork())
                {
                    DebiCheckMandate mandate = new XPQuery<DebiCheckMandate>(uow).FirstOrDefault(a => a.ApplicationId == ApplicationId);

                    var debiCheckMandate = new XPQuery<DebiCheckMandate>(uow).Where(x => x.ContractNumber == mandate.ContractNumber && x.Status == "CREATED").FirstOrDefault();
                    if (debiCheckMandate != null)
                    {
                        ApproveDebicheckDebitOrder(debiCheckMandate.ApplicationId);
                    }
                }
            }
            catch (Exception ex)
            {
                log.Error(string.Format("ApproveMandateAsync faild for application id :", ApplicationId));
                log.Error(ex.Data);
            }

        }
        public async Task<Response<string>> fn_CreateDebicheckDebitOrder(int applicationId)
        {
            log.Info("[fn_CreateDebicheckDebitOrder] function started for : " + applicationId);
            DebiCheckIntegration ob = new DebiCheckIntegration();
            ob.GetTokenAsync();
            ApplicationDto pApplicationDto = null;
            try
            {
                log.Info(string.Format("Get application details by id: {0}", applicationId));
                var role = this.HasAccess(BO_Object.Applications, BackOfficeEnum.Action.VIEW);
                if (role.Status == Constants.Success)
                {
                    DocumentsDto mDocDto = new DocumentsDto();
                    ApplicationDto info = new ApplicationDto();
                    using (var result = new BackOfficeWebServiceClient("BackOfficeWebServer.NET"))
                    {
                        var filterConditions = FilterUtil.GetFiltersAsString(BO_Object.Applications, Convert.ToInt32(HttpContext.Current.Session["UserId"]));
                        info = result.GetApplicationDetail(filterConditions, applicationId);
                        pApplicationDto = info;
                    }
                }
            }
            catch (Exception ex)
            {
                log.Error(string.Format("Get application details by id: {0} fail", applicationId));
            }

            try
            {

                using (var uow = new UnitOfWork())
                {

                    BNK_Integration bank = new XPQuery<BNK_Integration>(uow).FirstOrDefault(b => b.BankId == pApplicationDto.BankDetail.Bank.BankId);
                    pApplicationDto.BankDetail.Bank.BankId = bank.RealPayBankId;
                    pApplicationDto.BankDetail.Bank.Code = bank.RealPayBankCode;

                }
                using (var uow = new UnitOfWork())
                {
                    var client = new XPQuery<DebiCheckClient>(uow).FirstOrDefault(a => a.IDNumber == pApplicationDto.ApplicationClient.IDNumber);
                    if (client == null)
                    {
                        var res = ob.AddClient(pApplicationDto);
                        if (res.APIResponse.Status == "SUCCESS")
                        {
                            {
                                DebiCheckClient debiCheckClient = new DebiCheckClient(uow);
                                List<ClientPostSuccessful> clients = res.ClientPostResponse.FirstOrDefault().Successful;
                                if (clients == null || clients.Count < 1)
                                {
                                    ClientPostFailed fail = res.ClientPostResponse.FirstOrDefault().Failed.FirstOrDefault();
                                    throw new Exception(fail.Failures.FirstOrDefault().FailureDescription);
                                }
                                ClientPostSuccessful clientPostResponse = res.ClientPostResponse.FirstOrDefault().Successful.FirstOrDefault();
                                debiCheckClient.ApplicationId = pApplicationDto.ApplicationId;
                                debiCheckClient.ClientNumber = clientPostResponse.ClientNumber;
                                debiCheckClient.ClientName = clientPostResponse.ClientName;
                                debiCheckClient.IDType = clientPostResponse.IDType;
                                debiCheckClient.IDNumber = clientPostResponse.IDNumber;
                                debiCheckClient.CellphoneNumber = clientPostResponse.CellphoneNumber;
                                debiCheckClient.EMail = clientPostResponse.EMail;
                                debiCheckClient.BankCode = clientPostResponse.BankCode;
                                debiCheckClient.BranchCode = clientPostResponse.BranchCode;
                                debiCheckClient.AccountType = clientPostResponse.AccountType;
                                debiCheckClient.AccountNumber = clientPostResponse.AccountNumber;
                                debiCheckClient.AccountHolderName = clientPostResponse.AccountHolderName;
                                debiCheckClient.EmployeeGroupCode = clientPostResponse.EmployeeGroupCode;
                                debiCheckClient.Save();
                                uow.CommitChanges();
                            }
                        }
                    }
                }

                using (var uow = new UnitOfWork())
                {
                    var mandate = new XPQuery<DebiCheckMandate>(uow).FirstOrDefault(a => a.ApplicationId == pApplicationDto.ApplicationId);
                    if (mandate == null)
                    {
                        var resp = await ob.AddMandate(pApplicationDto);
                        if (resp.APIResponse.Status == "SUCCESS")
                        {
                            List<MandatePostSuccessful> clients = resp.MandatePostResponse.FirstOrDefault().Successful;
                            if (clients == null || clients.Count < 1)
                            {
                                log.Info("execute RejectApplication for Debicheck mandate failure");
                                string comment = "Application rejected due to incorrect Bank Account details";
                                var result = RejectApplication(applicationId, comment, BO_ObjectAPI.bankdetails.ToString());

                                var data = "{\"Comment\": \"" + comment + "\"}";
                                UpdateApplicationEventHistory(applicationId, "CHECK_DIGIT", data, "", BO_ObjectAPI.bankdetails);
                                
                                MandatePostFailed fail = resp.MandatePostResponse.FirstOrDefault().Failed.FirstOrDefault();
                                throw new Exception(fail.Failures.FirstOrDefault().FailureDescription);
                            }
                            MandatePostSuccessful debiCheckMandateDto = resp.MandatePostResponse.FirstOrDefault().Successful.FirstOrDefault();
                            DebiCheckMandate debiCheckMandate = new DebiCheckMandate(uow);
                            debiCheckMandate.ApplicationId = pApplicationDto.ApplicationId;
                            debiCheckMandate.MandateType = debiCheckMandateDto.MandateType;
                            debiCheckMandate.TransactionType = debiCheckMandateDto.TransactionType;
                            debiCheckMandate.MandateActionDate = debiCheckMandateDto.MandateActionDate;
                            debiCheckMandate.FrequencyCode = debiCheckMandateDto.FrequencyCode;
                            debiCheckMandate.CollectionDay = debiCheckMandateDto.CollectionDay;
                            debiCheckMandate.DebitSequenceType = debiCheckMandateDto.DebitSequenceType;
                            debiCheckMandate.AdjustmentCategory = debiCheckMandateDto.AdjustmentCategory;
                            debiCheckMandate.AdjustmentAmount = debiCheckMandateDto.AdjustmentAmount;
                            debiCheckMandate.AdjustmentRate = debiCheckMandateDto.AdjustmentRate;
                            debiCheckMandate.TrackingYN = debiCheckMandateDto.TrackingYN;
                            debiCheckMandate.TrackingCode = debiCheckMandateDto.TrackingCode;
                            debiCheckMandate.ClientNumber = debiCheckMandateDto.ClientNumber;
                            debiCheckMandate.ContractNumber = debiCheckMandateDto.ContractNumber;
                            debiCheckMandate.FirstCollectionDate = debiCheckMandateDto.FirstCollectionDate;
                            debiCheckMandate.FirstCollectionAmount = debiCheckMandateDto.FirstCollectionAmount;
                            debiCheckMandate.InstalmentStartDate = debiCheckMandateDto.InstalmentStartDate;
                            debiCheckMandate.InstalmentAmount = debiCheckMandateDto.InstalmentAmount;
                            debiCheckMandate.MaximumAmount = debiCheckMandateDto.MaximumAmount;
                            debiCheckMandate.NumberOfInstalments = debiCheckMandateDto.NumberOfInstalments;
                            debiCheckMandate.ContractSequence = debiCheckMandateDto.ContractSequence;
                            debiCheckMandate.Status = "CREATED";
                            debiCheckMandate.CreateDate = DateTime.Now;
                            debiCheckMandate.Save();
                            uow.CommitChanges();

                            //  Add records in standard Debit Order table too
                            Atlas.Domain.DTO.Account.ACC_DebitOrderDTO dbtOrder = new Atlas.Domain.DTO.Account.ACC_DebitOrderDTO();

                            dbtOrder = new Atlas.Domain.DTO.Account.ACC_DebitOrderDTO()
                            {
                                AccountId = Convert.ToInt64(pApplicationDto.AccountId),
                                accountNumber = pApplicationDto.BankDetail.AccountNo,
                                accountType = "Cheque",
                                adjRule = "Move Fwd",
                                approvalCode = "",
                                contractAmount = pApplicationDto.Quotation.LoanAmount,
                                frequency = pApplicationDto.Employer.SalaryType.ToString(),
                                pAN = "4451470012438140",
                                responseCode = "00-Approved or completed successfully",
                                tracking = "3 days",
                                transactionID = debiCheckMandateDto.ContractNumber,
                                ContractNumber = debiCheckMandateDto.ContractNumber
                            };

                            SaveDebitOrderResponse(dbtOrder);
                        }
                        else
                        {
                            log.Info("execute RejectApplication for Debicheck mandate failure");
                            string comment = "Application rejected due to incorrect Bank Account details";
                            var result = RejectApplication(applicationId, comment, BO_ObjectAPI.bankdetails.ToString());

                            var data = "{\"Comment\": \"" + comment + "\"}";
                            UpdateApplicationEventHistory(applicationId, "CHECK_DIGIT", data, "", BO_ObjectAPI.bankdetails);
                        }
                    }
                }
                log.Info("[fn_CreateDebicheckDebitOrder] function end for : " + applicationId);
                return Response<string>.CreateResponse(Constants.Success, null, null);
            }
            catch (Exception ex)
            {
                log.Info(ex);
                return Response<string>.CreateResponse(Constants.Failed, null, null);
            }
        }

        public void CreateABSABatchFile()
        {
            // To check whether any REPLY is pending from ABSA for SENT file
            // If so, do not create new ABSA Input file
            using (var uow = new UnitOfWork())
            {
                try
                {
                    ABSAFileHeader absaFileHeader = new XPQuery<ABSAFileHeader>(uow).FirstOrDefault(a => a.FileStatus == "SENT");
                    if (absaFileHeader != null)
                    {
                        log.Info(string.Format("ABSA file pending for reply."));
                        return;
                    }
                }
                catch (Exception ex)
                {
                    log.Error(string.Format("ABSA Batch file is not generated."));
                    return;
                }
            }

            //=============================
            int fileTransmissionNumber = 0;
            int firstSequenceNumber = 0;
            int userGenerationNumber = 0;

            Condition condition = null;

            // Find whether REJECTED file is available ?
            // If available, take required values
            List<VMApplication> applications = null;
            using (var uow = new UnitOfWork())
            {                
                var rejectedFile = new XPQuery<ABSAFileHeader>(uow).FirstOrDefault(a => a.FileStatus == "REJECTED");
                if (rejectedFile != null)
                {
                    fileTransmissionNumber = rejectedFile.TransmissionNumber;

                    firstSequenceNumber = rejectedFile.FirstSequenceNumber;
                    DBManager.SequenaceReset("UserSequence", firstSequenceNumber+1);

                    userGenerationNumber = rejectedFile.UserGenerationNumber;

                    // Update Applications and set back Status to STATUS_DEBIT_ORDER_APPROVED for the 
                    // set of applications which are in this REJECTED batch

                    var transactionRecords = new XPQuery<ABSATransaction>(uow).Where(a => a.TransmissionNumber == fileTransmissionNumber && a.ApplicationId > 0);
                    foreach (var application in transactionRecords)
                    {
                        using (var result = new BackOfficeWebServiceClient("BackOfficeWebServer.NET"))
                        {
                            var filterConditions = "1=1";
                            var StatusResult = result.UpdateApplicationDisbursementStatus(filterConditions, application.ApplicationId, (int)BackOfficeEnum.NewStatus.STATUS_DEBIT_ORDER_APPROVED);
                        }
                        // DBManager.UpdateApplicationCMSState(application.ApplicationId, BackOfficeEnum.Action.APPLICATION_INITIATE_DISBURSEMENT.ToString());
                    }

                    // Delete existing Data from 3 ABSA files for the REJECTED file status, by comparing TransmissionNumber

                    var absafileheader = new XPQuery<ABSAFileHeader>(uow).Where(a => a.TransmissionNumber == fileTransmissionNumber);
                    foreach (var rec in absafileheader)
                    {
                        rec.Delete();
                    }
                    var absatrailer= new XPQuery<ABSATrailer>(uow).Where(a => a.TransmissionNumber == fileTransmissionNumber);
                    foreach (var rec in absatrailer)
                    {
                        rec.Delete();
                    }
                    var absatransaction = new XPQuery<ABSATransaction>(uow).Where(a => a.TransmissionNumber == fileTransmissionNumber);
                    foreach (var rec in absatransaction)
                    {
                        rec.Delete();
                    }
                    uow.CommitChanges();

                    applications = DBManager.GetApplicationView("ApplicationsForABSACreate", 1, ref condition);
                }
                else
                {
                    applications = DBManager.GetApplicationView("ApplicationsForABSACreate", 1, ref condition);
                    if (applications == null || applications.Count == 0)
                    {
                        return;
                    }

                    fileTransmissionNumber = DBManager.GetTransmissionNumberSequence();
                    firstSequenceNumber = DBManager.GetUserSequence();
                    userGenerationNumber = DBManager.GetUserGenerationrSequence();
                }
            }

            //=============================

            try
            {
                log.Info(string.Format("ABSA Batch Record Insertion and Batch file generation started"));
                    using (var uow = new UnitOfWork())
                    {
                        try
                        {
                            //--- Get all applications whose Debicheck mandate status is True 
                            // Please modify the STATUS Enum to newly created one

                            if (applications.Count > 0)
                            {
                                //--- Insert one row into ABSAFileHeader
                                log.Info(string.Format("Get ABSA Master details from ABSAMaster"));
                                ABSAMaster aBSAMaster = new XPQuery<ABSAMaster>(uow).FirstOrDefault();
                                string recordIdentifier = aBSAMaster.RecordIdentifier;
                                log.Info(string.Format("Insert one record into ABSAFileHeader"));
                                ABSAFileHeader aBSAFileHeader = new ABSAFileHeader(uow);
                                // aBSAFileHeader.TransmissionNumber = DBManager.GetTransmissionNumberSequence();
                                aBSAFileHeader.TransmissionNumber = fileTransmissionNumber;
                                // aBSAFileHeader.RecordIdentifier = "060";
                                aBSAFileHeader.RecordIdentifier = recordIdentifier;

                                //aBSAFileHeader.DataSetStatus = "T";
                                aBSAFileHeader.DataSetStatus = aBSAMaster.DataSetStatus;

                                aBSAFileHeader.BankservRecordIdentifier = "04";
                                aBSAFileHeader.BankservUserCode = aBSAMaster.BankservUserCode;
                                aBSAFileHeader.BankservGlCreationDate = DateTime.Now;
                                aBSAFileHeader.BankservPurgeDate = DateTime.Now;
                                aBSAFileHeader.FirstActionDate = DateTime.Now;
                                aBSAFileHeader.LastActionDate = DateTime.Now;
                                // aBSAFileHeader.FirstSequenceNumber = DBManager.GetUserSequence();
                                aBSAFileHeader.FirstSequenceNumber = firstSequenceNumber;
                                // aBSAFileHeader.UserGenerationNumber = DBManager.GetUserGenerationrSequence();
                                aBSAFileHeader.UserGenerationNumber = userGenerationNumber;
                                //aBSAFileHeader.TypeOfService = "BATCH";
                                aBSAFileHeader.TypeOfService = ConfigurationManager.AppSettings["AbsaTypeOfService"];
                                aBSAFileHeader.FileStatus = "PENDING";
                                aBSAFileHeader.Save();

                                //---- Inserting Transaction records (Multiple)
                                log.Info(string.Format("Inserting Transaction records into ABSATransaction"));

                                int TotalAmount = 0;          // use in Contra
                                int TotalDebitRecords = 0;    // TotalDebitRecords = NoOfDebitTransRecords + NoOfCreditContraRecords   --- used in Trailer
                                int TotalCreditRecords = 0;   // TotalCreditRecords = NoOfCreditTransRecords + NoOfDebitContraRecords   --- used in Trailer
                                int TotalDebitValue = 0;      // Total value of Debit transactions including the Credit contra   --- used in Trailer
                                int TotalCreditValue = 0;     // Total value of Credit transactions including the Debit contra   --- used in Trailer
                                long SumOf_HAN_NHAN = 0;      // For the calculation of HashTotal (HTHA150)   --- used in Trailer

                                Boolean isFirstRecord = true;
                                foreach (var application in applications)
                                {
                                    int pApplicationId = application.ApplicationId;

                                    ApplicationDto pApplicationDto = null;
                                    DocumentsDto mDocDto = new DocumentsDto();
                                    ApplicationDto info = new ApplicationDto();
                                    using (var result = new BackOfficeWebServiceClient("BackOfficeWebServer.NET"))
                                    {
                                        var filterConditions = "1=1";
                                        pApplicationDto = result.GetApplicationDetail(filterConditions, pApplicationId);
                                    }

                                    ABSATransaction aBSATransaction = new ABSATransaction(uow);
                                    aBSATransaction.TransmissionNumber = aBSAFileHeader.TransmissionNumber;
                                    aBSATransaction.ApplicationId = pApplicationId;
                                    //aBSATransaction.RecordIdentifier = "060";
                                    aBSATransaction.RecordIdentifier = recordIdentifier;
                                    //aBSATransaction.DataSetStatus = "T";
                                    aBSATransaction.DataSetStatus = aBSAMaster.DataSetStatus;
                                    aBSATransaction.BankservRecordIdentifier = "10";
                                    aBSATransaction.UserBranch = aBSAMaster.UserBranch;
                                    aBSATransaction.UserNominatedAccount = aBSAMaster.UserNominatedAccount;
                                    aBSATransaction.UserCode = aBSAMaster.BankservUserCode;

                                    if (isFirstRecord)
                                    {
                                        aBSATransaction.UserSequenceNumber = aBSAFileHeader.FirstSequenceNumber;
                                    }
                                    else
                                    {
                                        aBSATransaction.UserSequenceNumber = DBManager.GetUserSequence();
                                    }

                                    aBSATransaction.HomingBranch = Convert.ToInt32(pApplicationDto.BankDetail.Bank.Code);
                                    aBSATransaction.HomingAccountNumber = pApplicationDto.BankDetail.AccountNo;
                                    aBSATransaction.TypeOfAccount = pApplicationDto.BankDetail.AccountType.AccountTypeId;
                                    aBSATransaction.Amount = (int)pApplicationDto.Affordability.LoanAmount;
                                    aBSATransaction.ActionDate = DateTime.Now;
                                    aBSATransaction.EntryClass = 88;
                                    aBSATransaction.TaxCode = 0;
                                    aBSATransaction.UserReference1 = "ATLFIN";
                                    aBSATransaction.UserReference2 = "";
                                    aBSATransaction.HomingAccountName = pApplicationDto.BankDetail.AccountName.ToUpper();
                                    aBSATransaction.NonStandardHomingAccountNumber = "0";
                                    aBSATransaction.HomingInstitution = 21;

                                    TotalAmount = TotalAmount + (int)pApplicationDto.Affordability.LoanAmount;
                                    TotalCreditRecords = TotalCreditRecords + 1;

                                    SumOf_HAN_NHAN += (long.Parse(aBSATransaction.HomingAccountNumber) + long.Parse(aBSATransaction.NonStandardHomingAccountNumber));

                                    aBSATransaction.Save();

                                    isFirstRecord = false;
                                }

                                //---- Inserting Contra Record
                                log.Info(string.Format("Inserting one Contra record into ABSATransaction"));
                                ABSATransaction Contra = new ABSATransaction(uow);
                                Contra.TransmissionNumber = aBSAFileHeader.TransmissionNumber;
                                // Contra.RecordIdentifier = "060";
                                Contra.RecordIdentifier = recordIdentifier;
                                // Contra.DataSetStatus = "T";
                                Contra.DataSetStatus = aBSAMaster.DataSetStatus;
                                Contra.BankservRecordIdentifier = "12";
                                Contra.UserBranch = aBSAMaster.UserBranch;
                                Contra.UserNominatedAccount = aBSAMaster.UserNominatedAccount;
                                // Contra.UserCode = aBSAMaster.UserCode.ToString();
                                Contra.UserCode = aBSAMaster.BankservUserCode;
                                Contra.UserSequenceNumber = DBManager.GetUserSequence();
                                Contra.HomingBranch = aBSAMaster.UserBranch;
                                Contra.HomingAccountNumber = aBSAMaster.UserNominatedAccount;
                                Contra.TypeOfAccount = 1; // must be 1
                                Contra.Amount = TotalAmount;
                                Contra.ActionDate = DateTime.Now;
                                Contra.EntryClass = 10;
                                Contra.TaxCode = 0;
                                Contra.UserReference1 = "ATLFIN";
                                Contra.UserReference2 = "";
                                Contra.HomingAccountName = "";
                                Contra.NonStandardHomingAccountNumber = "";
                                Contra.HomingInstitution = 21;
                                Contra.Save();

                                TotalDebitRecords = 1;
                                TotalDebitValue = Contra.Amount.Value;
                                TotalCreditValue = TotalAmount;

                                //---- Inserting Trailer Record

                                log.Info(string.Format("Inserting one Trailer record into ABSATrailer"));

                                long LsdOfSumOf_HAN_NHAN = SumOf_HAN_NHAN % 100000000000;    // 11 digit LSD of SumOfAll(HomingAccountNumber + Non-standardHomingAccountNumber)
                                string mHTHA150 = (LsdOfSumOf_HAN_NHAN + long.Parse(Contra.HomingAccountNumber)).ToString().PadLeft(12, '0');   // final 12 digits of HASH total as per ABSA formula

                                ABSATrailer aBSATrailer = new ABSATrailer(uow);
                                aBSATrailer.TransmissionNumber = aBSAFileHeader.TransmissionNumber;
                                // aBSATrailer.RecordIdentifier = "060";
                                aBSATrailer.RecordIdentifier = recordIdentifier;
                                // aBSATrailer.DataSetStatus = "T";
                                aBSATrailer.DataSetStatus = aBSAMaster.DataSetStatus;
                                aBSATrailer.BankservRecordIdentifier = "92";
                                aBSATrailer.BankservUserCode = aBSAMaster.BankservUserCode;
                                aBSATrailer.FirstSequenceNumber = aBSAFileHeader.FirstSequenceNumber;
                                aBSATrailer.LastSequenceNumber = Contra.UserSequenceNumber;
                                aBSATrailer.FirstActionDate = DateTime.Now;
                                aBSATrailer.LastActionDate = DateTime.Now;
                                aBSATrailer.NoOfDebitRecords = TotalDebitRecords;
                                aBSATrailer.NoOfCreditRecords = TotalCreditRecords;
                                aBSATrailer.NoOfContraRecords = 1;
                                aBSATrailer.TotalDebit = TotalDebitValue;
                                aBSATrailer.TotalCredit = TotalCreditValue;
                                aBSATrailer.HTHA150 = mHTHA150;
                                aBSATrailer.Save();

                                uow.CommitChanges();
                                log.Info(string.Format("ABSA Records inserted successfully"));

                                // Calling method CreateABSATransmissionFile to create TXT file of ABSA Input

                                var res = CreateABSATransmissionFile(aBSAFileHeader.TransmissionNumber);

                                aBSAFileHeader.FileStatus = "SENT";
                                aBSAFileHeader.Save();
                                uow.CommitChanges();

                                foreach (var application in applications)
                                {
                                    using (var result = new BackOfficeWebServiceClient("BackOfficeWebServer.NET"))
                                    {
                                        var filterConditions = "1=1";
                                        var StatusResult = result.UpdateApplicationDisbursementStatus(filterConditions, application.ApplicationId, (int)BackOfficeEnum.NewStatus.STATUS_DISBURSEMENT_INITIATED);
                                    }
                                    DBManager.UpdateApplicationCMSState(application.ApplicationId, BackOfficeEnum.Action.APPLICATION_INITIATE_DISBURSEMENT.ToString());
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            log.Error(string.Format("Get applications failed"));
                        }
                    }
            }
            catch (Exception ex)
            {
                log.Error(string.Format("ABSA Batch file generation Failed"));
            }
        }


        //========================== for ABSA file generation

        public Response<string> CreateABSATransmissionFile(int transmissionNumber)
        {
            try
            {
                log.Info(string.Format("Getting records for ABSA file"));

                    string mABSAstring;

                    using (var uow = new UnitOfWork())
                    {
                        var absaFileHeader = new XPQuery<ABSAFileHeader>(uow).Where(x => x.TransmissionNumber == transmissionNumber).FirstOrDefault();

                        var mTransmissionHeaderString = GetTransmissionHeaderRecord(absaFileHeader.TransmissionNumber);
                        var mFileHeaderString = GetFileHeaderRecord(absaFileHeader.TransmissionNumber);
                        var mTransactionString = GetTransactionRecord(absaFileHeader.TransmissionNumber);
                        var mContraString = GetContraRecord(absaFileHeader.TransmissionNumber);
                        var mTrailerString = GetTrailerRecord(absaFileHeader.TransmissionNumber);
                        var mTransFooterString = GetTransmissionTrailer(absaFileHeader.TransmissionNumber);

                        mABSAstring = mTransmissionHeaderString
                                + "\r\n"
                                + mFileHeaderString
                                + "\r\n"
                                + mTransactionString
                                + mContraString
                                + "\r\n"
                                + mTrailerString
                                + "\r\n"
                                + mTransFooterString
                                + "\r\n";
                    }

                string fileName = "INPUT." + DateTime.Now.ToString("MMddhhmmss");
                string filePath = ConfigurationManager.AppSettings["AbsaINPUTPath"] + fileName;
                File.WriteAllText(filePath, mABSAstring);

                    return Response<string>.CreateResponse("ABSA File generated successfully.", null, null); ;
            }
            catch (Exception ex)
            {
                log.Error(string.Format("Getting ABSA records error: {0}", ex.ToString()));
                return Response<string>.CreateResponse(Constants.Failed, null, new ErrorHandler
                {
                    ErrorCode = Constants.AccountErrorCode,
                    Message = Constants.AccountDetailsErrorMessage
                });
            }
        }

        public string GetTransmissionHeaderRecord(int transmissionNumber)
        {
            try
            {
                log.Info(string.Format("Creating TransmissionHeader Record for ABSA file"));
                    using (var uow = new UnitOfWork())
                    {
                        var absaTransmissionHeader = new XPQuery<ABSAMaster>(uow).FirstOrDefault();
                        var absaFileHeader = new XPQuery<ABSAFileHeader>(uow).Where(x => x.TransmissionNumber == transmissionNumber && x.FileStatus == "PENDING").FirstOrDefault();
                        
                        string RecordIdentifier = "000"; // 3

                        // string DataSetStatus = "T"; //  DataSetStatus
                        string DataSetStatus = absaFileHeader.DataSetStatus;
                        
                        string TransmissionDate = DateTime.Now.ToString("yyyyMMdd"); // 8
                        string ElectronicBankingSuiteUserCode = absaTransmissionHeader.UserCode.ToString().PadLeft(5,'0'); // 5  ------- ???
                        string ElectronicBankingSuiteUserName = absaTransmissionHeader.UserName.PadRight(30); // 30
                        string TransmissionNumber = absaFileHeader.TransmissionNumber.ToString().PadLeft(7, '0'); //   7
                        string Destination = "00000"; //    5
                        string FILLER1 = ' '.Repeat(119);  // 119
                        string UserComment = ' '.Repeat(20); // 20

                        string TransmissionHeaderString = RecordIdentifier +
                            DataSetStatus +
                            TransmissionDate +
                            ElectronicBankingSuiteUserCode +
                            ElectronicBankingSuiteUserName +
                            TransmissionNumber +
                            Destination +
                            FILLER1 +
                            UserComment;

                        return TransmissionHeaderString;
                    }
            }
            catch (Exception ex)
            {
                return null;
            }
        }


        public string GetFileHeaderRecord(int mTransmissionNumber)
        {
            try
            {
                log.Info(string.Format("Creating FileHeader Record for ABSA file"));
                    using (var uow = new UnitOfWork())
                    {
                        var absaFileHeader = new XPQuery<ABSAFileHeader>(uow).Where(x => x.TransmissionNumber == mTransmissionNumber && x.FileStatus == "PENDING").FirstOrDefault();

                        string FILLER1 = ' '.Repeat(142);

                        var FileHeaderString = absaFileHeader.RecordIdentifier
                            + absaFileHeader.DataSetStatus
                            + absaFileHeader.BankservRecordIdentifier
                            + absaFileHeader.BankservUserCode.PadRight(4)
                            + absaFileHeader.BankservGlCreationDate.ToString("yyMMdd")
                            + absaFileHeader.BankservPurgeDate.ToString("yyMMdd")
                            + absaFileHeader.FirstActionDate.ToString("yyMMdd")
                            + absaFileHeader.LastActionDate.ToString("yyMMdd")
                            + absaFileHeader.FirstSequenceNumber.ToString().PadLeft(6,'0')
                            + absaFileHeader.UserGenerationNumber.ToString().PadLeft(4,'0')
                            + absaFileHeader.TypeOfService.PadRight(10)
                            + "YY"
                            + FILLER1;

                        return FileHeaderString;
                    }
            }
            catch (Exception ex)
            {
                return null;
            }
        }

        public string GetTransactionRecord(int mTransmissionNumber)
        {
            try
            {
                log.Info(string.Format("Creating Transaction Records for ABSA file"));
                    using (var uow = new UnitOfWork())
                    {
                        var TransactionString = "";
                        List<ABSATransaction> absaTransactionList = new XPQuery<ABSATransaction>(uow).Where(x => x.TransmissionNumber == mTransmissionNumber && x.BankservRecordIdentifier == "10").ToList();
                        if (absaTransactionList.Count > 0 && absaTransactionList != null)
                        {
                            foreach (ABSATransaction absaTransaction in absaTransactionList)
                            {
                                string FILLER2 = ' '.Repeat(27);

                                TransactionString = TransactionString
                                    + absaTransaction.RecordIdentifier
                                    + absaTransaction.DataSetStatus
                                    + absaTransaction.BankservRecordIdentifier
                                    + absaTransaction.UserBranch.ToString().PadLeft(6, '0')
                                    + absaTransaction.UserNominatedAccount.PadLeft(11, '0')
                                    + absaTransaction.UserCode.PadRight(4)
                                    + absaTransaction.UserSequenceNumber.ToString().PadLeft(6, '0')
                                    + absaTransaction.HomingBranch.ToString().PadLeft(6, '0')
                                    + absaTransaction.HomingAccountNumber.PadLeft(11, '0')
                                    + absaTransaction.TypeOfAccount
                                    + (absaTransaction.Amount*100).ToString().PadLeft(11, '0')
                                    + absaTransaction.ActionDate.ToString("yyMMdd")
                                    + absaTransaction.EntryClass.ToString().PadLeft(2, '0')
                                    + absaTransaction.TaxCode.ToString().PadLeft(1, '0')
                                    + ' '.Repeat(3)
                                    + absaTransaction.UserReference1.PadRight(10)
                                    + absaTransaction.UserReference2.PadRight(20)
                                    + absaTransaction.HomingAccountName.PadRight(30)
                                    + absaTransaction.NonStandardHomingAccountNumber.PadLeft(20, '0')
                                    + ' '.Repeat(16)
                                    + absaTransaction.HomingInstitution.ToString().PadLeft(2, '0')
                                    + ' '.Repeat(26)
                                    + "\r\n";
                            }
                        }
                        return TransactionString;
                    }
            }
            catch (Exception ex)
            {
                return null;
            }
        }

        public string GetContraRecord(int mTransmissionNumber)
        {
            try
            {
                log.Info(string.Format("Creating Contra Record for ABSA file"));
                    using (var uow = new UnitOfWork())
                    {
                        var absaTransaction = new XPQuery<ABSATransaction>(uow)
                            .Where(x => x.TransmissionNumber == mTransmissionNumber && x.BankservRecordIdentifier == "12" ).FirstOrDefault();

                        string FILLER3 = '0'.Repeat(4);
                        string FILLER4 = ' '.Repeat(30);
                        string FILLER5 = ' '.Repeat(64);

                        var ContraString = absaTransaction.RecordIdentifier.PadLeft(3, '0')
                            + absaTransaction.DataSetStatus
                            + absaTransaction.BankservRecordIdentifier
                            + absaTransaction.UserBranch.ToString().PadLeft(6, '0')
                            + absaTransaction.UserNominatedAccount.PadLeft(11, '0')
                            + absaTransaction.UserCode.PadRight(4)
                            + absaTransaction.UserSequenceNumber.ToString().PadLeft(6, '0')
                            + absaTransaction.HomingBranch.ToString().PadLeft(6, '0')
                            + absaTransaction.HomingAccountNumber.PadLeft(11, '0')
                            + absaTransaction.TypeOfAccount.ToString().PadLeft(1, '0')
                            + (absaTransaction.Amount*100).ToString().PadLeft(11, '0')
                            + absaTransaction.ActionDate.ToString("yyMMdd")
                            + absaTransaction.EntryClass.ToString().PadLeft(2, '0')
                            + FILLER3
                            + absaTransaction.UserReference1.PadRight(10)
                            + "CONTRA"
                            + absaTransaction.UserReference2.PadRight(14)
                            + FILLER4
                            + FILLER5;

                        return ContraString;
                    }
            }
            catch (Exception ex)
            {
                return null;
            }
        }

        public string GetTrailerRecord(int mTransmissionNumber)
        {
            try
            {
                log.Info(string.Format("Creating Trailer Record for ABSA file"));
                    using (var uow = new UnitOfWork())
                    {
                        var absaTrailer = new XPQuery<ABSATrailer>(uow).Where(x => x.TransmissionNumber == mTransmissionNumber).FirstOrDefault();

                        string FILLER6 = ' '.Repeat(110);

                        var mTrailerString = absaTrailer.RecordIdentifier
                            + absaTrailer.DataSetStatus
                            + absaTrailer.BankservRecordIdentifier
                            + absaTrailer.BankservUserCode.PadRight(4)
                            + absaTrailer.FirstSequenceNumber.ToString().PadLeft(6,'0')
                            + absaTrailer.LastSequenceNumber.ToString().PadLeft(6, '0')
                            + absaTrailer.FirstActionDate.ToString("yyMMdd")
                            + absaTrailer.LastActionDate.ToString("yyMMdd")
                            + absaTrailer.NoOfDebitRecords.ToString().PadLeft(6, '0')
                            + absaTrailer.NoOfCreditRecords.ToString().PadLeft(6, '0')
                            + absaTrailer.NoOfContraRecords.ToString().PadLeft(6, '0')
                            + (absaTrailer.TotalDebit*100).ToString().PadLeft(12, '0')
                            + (absaTrailer.TotalCredit*100).ToString().PadLeft(12, '0')
                            + absaTrailer.HTHA150.PadLeft(12, '0')
                            + FILLER6;

                        return mTrailerString;
                    }
            }
            catch (Exception ex)
            {
                return null;
            }
        }


        public string GetTransmissionTrailer(int mTransmissionNumber)
        {
            try
            {
                log.Info(string.Format("Creating TransmissionTrailer Record for ABSA file"));
                int TotalRecords = 0;
                string DataSetStatus;
                using (var uow = new UnitOfWork())
                {
                    var absaTransactionRowCount = new XPQuery<ABSATransaction>(uow).Where(x => x.TransmissionNumber == mTransmissionNumber).Count();
                    TotalRecords = absaTransactionRowCount + 4;

                    ABSAMaster aBSAMaster = new XPQuery<ABSAMaster>(uow).FirstOrDefault();
                    DataSetStatus = aBSAMaster.DataSetStatus;
                }

                string FILLER1 = ' '.Repeat(185);

                string RecordIdentifier = "999";

                //string TtNoOfRecs = "000000006";

                var mTransFooterString = RecordIdentifier
                    + DataSetStatus
                    + TotalRecords.ToString().PadLeft(9, '0')
                    + FILLER1;

                return mTransFooterString;
            }
            catch (Exception ex)
            {
                return null;
            }
        }

        public Response<string> RejectDocument(int ApplicationId, string RejectReason)
        {
            JObject payLoad = new JObject(
                new JProperty("RejectReason", RejectReason)
            );

            //var url = "http://localhost:5000/flowfinance/Document/RejectDocuments/" + ApplicationId;
            var url = ConfigurationManager.AppSettings["ff_baseurl"] + "Document/RejectDocuments/" + ApplicationId;

            HttpClient httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Add("Authorization", ConfigurationManager.AppSettings["ff_authorization"]);
            var response = httpClient.PostAsync(url, new StringContent(payLoad.ToString(), Encoding.UTF8, "application/json")).Result;

            log.Info("Documents Rejected");
            return null;
        }

        public Response<string> RejectNationalID()
        {
            log.Info("National ID Proof Rejected");
            return null;
        }

        public Response<string> RejectEmployer(int ApplicationId, string RejectReason)
        {
            JObject payLoad = new JObject(
                new JProperty("RejectReason", RejectReason)
            );

            // var url = "http://localhost:5000/flowfinance/EmployerDetail/Reject/" + ApplicationId;
            var url = ConfigurationManager.AppSettings["ff_baseurl"] + "EmployerDetail/Reject/" + ApplicationId;

            HttpClient httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Add("Authorization", ConfigurationManager.AppSettings["ff_authorization"]);
            var response = httpClient.PostAsync(url, new StringContent(payLoad.ToString(), Encoding.UTF8, "application/json")).Result;

            log.Info("Employer Rejected");
            return Response<string>.CreateResponse(Constants.Success, null, null);
        }


        public Response<string> RejectApplication(int ApplicationId, string RejectReason, string category)
        {
            var rejectStatusId = 4 ;
            var url = "";

            using (var uow = new UnitOfWork())
            {
                using (var result = new BackOfficeWebServiceClient("BackOfficeWebServer.NET"))
                {
                    bool res = false;
                    int appId = Convert.ToInt32(ApplicationId);
                    var filterConditions = FilterUtil.GetFiltersAsString(BO_Object.Applications, Convert.ToInt32(HttpContext.Current.Session["UserId"]));

                    // for Clent object (personal details)
                    if (category == BO_ObjectAPI.client.ToString())
                    {
                        res = result.UpdateApplicationClientStatus(filterConditions, appId, rejectStatusId);
                        url = ConfigurationManager.AppSettings["ff_baseurl"] + "PersonalDetails/Reject/" + ApplicationId;
                    }

                    // for Affordability
                    if (category == BO_ObjectAPI.affordability.ToString())
                    {
                        res = result.UpdateApplicationAffordabilityStatus(filterConditions, appId, rejectStatusId);
                        url = ConfigurationManager.AppSettings["ff_baseurl"] + "IncomeExpense/Reject/" + ApplicationId;
                    }

                    // for Bank details
                    if (category == BO_ObjectAPI.bankdetails.ToString())
                    {
                        res = result.UpdateApplicationBankStatus(filterConditions, appId, rejectStatusId);
                        url = ConfigurationManager.AppSettings["ff_baseurl"] + "BankDetail/Reject/" + ApplicationId;
                    }

                    // for Employer details
                    if (category == BO_ObjectAPI.employerdetails.ToString())
                    {
                        res = result.ChangeApplicationEmployerStatus(filterConditions, appId, rejectStatusId);
                        url = ConfigurationManager.AppSettings["ff_baseurl"] + "EmployerDetail/Reject/" + ApplicationId;
                    }

                    // for Document details
                    if (category == BO_ObjectAPI.documents.ToString())
                    {
                        res = result.UpdateApplicationDocumentStatus(filterConditions, appId, rejectStatusId);
                        url = ConfigurationManager.AppSettings["ff_baseurl"] + "Document/RejectDocuments/" + ApplicationId;
                    }

                    // for Quotation
                    if (category == BO_ObjectAPI.quotation.ToString())
                    {
                        res = result.UpdateApplicationQuotationStatus(filterConditions, appId, rejectStatusId);
                        url = ConfigurationManager.AppSettings["ff_baseurl"] + "Quotation/Reject/" + ApplicationId;
                    }
                }
            }

            JObject payLoad = new JObject(
                new JProperty("RejectReason", RejectReason)
                );

            HttpClient httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Add("Authorization", ConfigurationManager.AppSettings["ff_authorization"]);
            var response = httpClient.PostAsync(url, new StringContent(payLoad.ToString(), Encoding.UTF8, "application/json")).Result;

            log.Info("Application Rejected");
            return Response<string>.CreateResponse(Constants.Success, null, null);
        }

        [HttpPost]
        [Route("Applications/GenerateDynamicQuotation")]
        public Response<string> GenerateDynamicQuotation()
        {
            try
            {
                log.Debug($"[GenerateDynamicQuotation] started executing");
                List<int> applicationIdList = DBManager.GetQuoAccePendApplications("GetQAccePendingApplications");
                if (applicationIdList.Count > 0)
                {
                    log.Debug($"[GenerateDynamicQuotation] list of applicationIds to generate the quotation {applicationIdList.ToString()}");
                    foreach (int applicationId in applicationIdList)
                    {
                        try
                        {
                            ApplicationDto info = new ApplicationDto();
                            using (var result = new BackOfficeWebServiceClient("BackOfficeWebServer.NET"))
                            {
                                var filterConditions = FilterUtil.GetFiltersAsString(BO_Object.Applications, 0);
                                info = result.GetApplicationDetail(filterConditions, applicationId);
                            }

                            if (info.Quotation.ExpiryDate < DateTime.Now.Date)
                            {
                                // Reject Application 
                                log.Info("execute RejectApplication due to Quotation Expiry");
                                string comment = "Application rejected due to Quotation Expiry";
                                var result = RejectApplication(applicationId, comment, BO_ObjectAPI.quotation.ToString());
                                var data = "{\"Comment\": \"" + comment + "\"}";
                                UpdateApplicationEventHistory(applicationId, "REJECT_SUBMITTED", data, "", BO_ObjectAPI.quotation);
                            }
                            else
                            {
                                // continue quotaion update

                                //VMPossibleLoanTermCalculation termcalculation =
                                //new VMPossibleLoanTermCalculation
                                //{
                                //    applicationId = applicationId,
                                //    loanAmount = info.Quotation.LoanAmount,
                                //    DiscretionAmount = info.Affordability.Discretion,
                                //    LoanDate = DateTime.Now.Date.ToString("yyyy-MM-dd"),
                                //    frequencyTypeId = info.Employer.AddressTypeId.AddressTypeId,
                                //    IsVAPChecked = info.Quotation.VAP,
                                //    IsInsuranceRequired = info.Quotation.LifeInsurance,
                                //    FirstRepaymentDate = null,
                                //    LastRepaymentDate = null
                                //};

                                //List<dynamic> calculation = GetPossibleLoanTermCalculation(termcalculation); 

                                BackOfficeServer.BackOfficeWebServer.VMQuotation quot = new BackOfficeServer.BackOfficeWebServer.VMQuotation();
                                quot.ApplicationId = applicationId;
                                quot.LoanDate = DateTime.Now.Date;
                                quot.RepaymentDate = quot.LoanDate.AddDays(info.Quotation.Period - 1);
                                using (var result = new BackOfficeWebServiceClient("BackOfficeWebServer.NET"))
                                {
                                    bool response = result.UpdateQuotation(quot);
                                    if (response == true)
                                    {
                                        log.Debug($"[GenerateDynamicQuotation] : Quotation generated for ApplicationId : {applicationId}");
                                        log.Debug($"[GenerateDynamicQuotation] : Quotation pdf creating for ApplicationId : {applicationId}");
                                        GetMultiplePdf(applicationId);
                                        log.Debug($"[GenerateDynamicQuotation] : Quotation pdf created for ApplicationId : {applicationId}");
                                        log.Debug($"[GenerateDynamicQuotation] : Creating Application case audit log");
                                        ApplicationCaseUtil.InsertApplicationCaseAuditLog(applicationId);
                                        log.Debug($"[GenerateDynamicQuotation] : Created Application case audit log");
                                    }
                                    else
                                    {
                                        log.Debug($"[GenerateDynamicQuotation] : Failed to generate quotation for ApplicationId : {applicationId}");
                                    }
                                }

                                // Send notification after regenerating the quotation
                                JObject payLoad = new JObject(
                                    new JProperty("Quote Status", "Quotation Updated")
                                    );
                                var url = ConfigurationManager.AppSettings["ff_baseurl"] + "Quotation/SendNotification/" + applicationId;
                                HttpClient httpClient = new HttpClient();
                                httpClient.DefaultRequestHeaders.Add("Authorization", ConfigurationManager.AppSettings["ff_authorization"]);
                                var resp = httpClient.PostAsync(url, new StringContent(payLoad.ToString(), Encoding.UTF8, "application/json")).Result;
                            }
                        }
                        catch (Exception ex)
                        {
                            log.Error($"[GenerateDynamicQuotation] : Error Found : {ex.Message} for applicationId : {applicationId}");
                        }
                    }
                }
                else
                {
                    log.Debug($"[GenerateDynamicQuotation] : no applications pending for quotation acceptance");
                    return Response<string>.CreateResponse(Constants.Success, null,new ErrorHandler { ErrorCode = "", Message = "No Applications Found" });
                }
                log.Debug($"[GenerateDynamicQuotation] ended");
                return Response<string>.CreateResponse(Constants.Success, null, null);

            }
            catch (Exception ex)
            {
                log.Debug($"[GenerateDynamicQuotation] ended");
                throw ex;
            }
        }

        public async Task ApproveAndSendQuote(int applicationId)
        {
           log.Info("ApproveAndSendQuote function started for application id : "+applicationId);
           try
           {
               var result = SendQuote(applicationId);
           }
           catch(Exception ex)
           {
               log.Info("Error in [ApproveAndSendQuote] function :"+ex);
           }
        }

        [HttpGet]
        [Route("PingSession/{id}")]
        public JObject PingSession(int id)
        {
            ValidateToken();

            dynamic jObject = new JObject();
            jObject.response = true;
            return jObject;
        }


        // To get values from ABSA reply files and Update status into ABSATransaction table
        // To get values from ABSA reply files and Update status into ABSATransaction table
        //public Response<string> UpdateAbsaStatus()
        //{
        //    try
        //    {
        //        var AbsaReplyFilePath = ConfigurationManager.AppSettings["AbsaREPLYPath"];
        //        var AbsaReplyFileArchivePath = ConfigurationManager.AppSettings["AbsaREPLY_ARCHIVEPath"];
        //        var di = new DirectoryInfo(AbsaReplyFilePath);
        //        //var fileInfos = di.EnumerateFiles("*.*", SearchOption.TopDirectoryOnly).Where(n => Path.GetExtension(n.Name).ToLower() == ".txt").OrderBy(n => n.CreationTime);
        //        var fileInfos = di.EnumerateFiles("REPLY.*", SearchOption.TopDirectoryOnly).OrderBy(n => n.CreationTime);
        //        if (fileInfos != null)
        //        {
        //            string mFilename = "";
        //            string[] lines;
        //            foreach (FileInfo fileInfo in fileInfos)
        //            {
        //                try
        //                {
        //                    mFilename = fileInfo.Name;
        //                    lines = File.ReadAllLines(AbsaReplyFilePath + mFilename);
        //                    foreach (var line in lines)
        //                    {
        //                        if (line.Substring(8, 2) == "10" && (line.Substring(0, 3) == "901" || line.Substring(0, 3) == "903"))
        //                        {
        //                            string mStatusCode_HAN = GetStatusHANFromAbsaFile(mFilename, AbsaReplyFilePath);
        //                            if (!string.IsNullOrEmpty(mStatusCode_HAN))
        //                            {
        //                                int mTransmissionNo = Int32.Parse(mStatusCode_HAN.Substring(0, 7));
        //                                string mStatusCode = mStatusCode_HAN.Substring(7, 3);
        //                                string mHAN = mStatusCode_HAN.Substring(10, 11);
        //                                int mUserSeqNo = Int32.Parse(mStatusCode_HAN.Substring(21, 6));
        //                                using (var uow = new UnitOfWork())
        //                                {
        //                                    try
        //                                    {
        //                                        // ABSATransaction AbsaTrans = new XPQuery<ABSATransaction>(uow).FirstOrDefault(a => a.TransmissionNumber == mTransmissionNo && a.HomingAccountNumber == mHAN && a.UserSequenceNumber == mUserSeqNo && a.BankservRecordIdentifier == "10");
        //                                        ABSATransaction AbsaTrans = new XPQuery<ABSATransaction>(uow).FirstOrDefault(a => a.TransmissionNumber == mTransmissionNo && a.UserSequenceNumber == mUserSeqNo && a.BankservRecordIdentifier == "10");
        //                                        if (AbsaTrans != null)
        //                                        {
        //                                            if (mStatusCode == "903")  //---- for ACCEPTED
        //                                            {
        //                                                AbsaTrans.AbsaStatus = "ACCEPTED";
        //                                                using (var result = new BackOfficeWebServiceClient("BackOfficeWebServer.NET"))
        //                                                {
        //                                                    var accountId = Disbursement(AbsaTrans.ApplicationId);
        //                                                    var ffdisburse = UpdateFlowFinanceDisbursement(AbsaTrans.ApplicationId, "AOL" + accountId.ToString().PadLeft(7, '0'));
        //                                                    if (accountId > 0)
        //                                                    {
        //                                                        fn_printAgree(AbsaTrans.ApplicationId);
        //                                                        var StatusResult = result.UpdateApplicationDisbursementStatus("1=1", AbsaTrans.ApplicationId, (int)BackOfficeEnum.NewStatus.STATUS_DISBURSED);
        //                                                    }
        //                                                }
        //                                            }
        //                                            else if (mStatusCode == "901")   //---- for REJECTED
        //                                            {
        //                                                AbsaTrans.AbsaStatus = "REJECTED";
        //                                            }
        //                                            AbsaTrans.Save();
        //                                            uow.CommitChanges();
        //                                        }
        //                                    }
        //                                    catch (Exception ex)
        //                                    {
        //                                        log.Info(ex.ToString());
        //                                        return Response<string>.CreateResponse(Constants.Failed, null, new ErrorHandler()
        //                                        { ErrorCode = Constants.AbsaStatusUpdateErrorCode, Message = Constants.AbsaStatusUpdateErrorMessage });
        //                                    }
        //                                }
        //                            }
        //                        }
        //                    }
        //                    //---- to move completed file to Archive folder
        //                    string sourceFile = Path.Combine(AbsaReplyFilePath, mFilename);
        //                    string destFile = Path.Combine(AbsaReplyFileArchivePath, mFilename);
        //                    if (!Directory.Exists(AbsaReplyFileArchivePath))
        //                    {
        //                        Directory.CreateDirectory(AbsaReplyFileArchivePath);
        //                    }
        //                    File.Move(sourceFile, destFile);
        //                }
        //                catch (Exception ex)
        //                {
        //                    log.Info(ex.ToString());
        //                    return Response<string>.CreateResponse(Constants.Failed, null, new ErrorHandler()
        //                    { ErrorCode = Constants.ApplicationErrorCode, Message = "Error updating ABSA Reply status" });
        //                }
        //            }
        //            return Response<string>.CreateResponse(Constants.Success, "ABSA reply status update process completed", null);
        //        }
        //        else
        //        {
        //            log.Info("Files not found...!");
        //            return Response<string>.CreateResponse(Constants.Failed, "Files not found...!", null);
        //        }
        //    }
        //    catch (Exception ex)
        //    {
        //        log.Info(ex.ToString());
        //        return Response<string>.CreateResponse(Constants.Failed, null, new ErrorHandler()
        //        { ErrorCode = Constants.ApplicationErrorCode, Message = Constants.ApplicationDetailsNotFound });
        //    }
        //}


        // To get values from ABSA reply files and Update status into ABSATransaction table
        // To get values from ABSA reply files and Update status into ABSATransaction table
        public Response<string> UpdateAbsaStatus()
        {
            try
            {
                var AbsaReplyFilePath = ConfigurationManager.AppSettings["AbsaREPLYPath"];
                var AbsaReplyFileArchivePath = ConfigurationManager.AppSettings["AbsaREPLY_ARCHIVEPath"];
                var di = new DirectoryInfo(AbsaReplyFilePath);
                //var fileInfos = di.EnumerateFiles("*.*", SearchOption.TopDirectoryOnly).Where(n => Path.GetExtension(n.Name).ToLower() == ".txt").OrderBy(n => n.CreationTime);
                var fileInfos = di.EnumerateFiles("REPLY.*", SearchOption.TopDirectoryOnly).OrderBy(n => n.CreationTime);
                if (fileInfos != null)
                {
                    string mFilename = "";
                    string[] lines;
                    foreach (FileInfo fileInfo in fileInfos)
                    {
                        try
                        {
                            mFilename = fileInfo.Name;
                            lines = File.ReadAllLines(AbsaReplyFilePath + mFilename);
                            int mTransmissionNo = Int32.Parse(lines[1].Substring(27, 7));
                            string mFileStatus = lines[1].Substring(35, 8);

                            /// Update FileStatus of ABSAFileHeader table to "ACCEPTED" or "REJECTED" as per the REPLY file status
                            using (var uow = new UnitOfWork())
                            {
                                try
                                {
                                    ABSAFileHeader absaFileHeader = new XPQuery<ABSAFileHeader>(uow).FirstOrDefault(a => a.TransmissionNumber == mTransmissionNo && a.FileStatus == "SENT");
                                    if (absaFileHeader != null)
                                    {
                                        absaFileHeader.FileStatus = mFileStatus;
                                        absaFileHeader.Save();
                                        uow.CommitChanges();
                                    }
                                }
                                catch (Exception ex)
                                {
                                    log.Info(ex.ToString());
                                    return Response<string>.CreateResponse(Constants.Failed, null, new ErrorHandler()
                                    { ErrorCode = Constants.AbsaStatusUpdateErrorCode, Message = Constants.AbsaStatusUpdateErrorMessage });
                                }
                            }

                            if (mFileStatus == "ACCEPTED")
                            {
                                foreach (var line in lines)
                                {
                                    if (line.Contains("***"))
                                    {
                                        continue;
                                    }

                                    if ((line.Substring(8, 2) == "10" && line.Substring(0, 3) == "903") || line.Substring(0, 3) == "901")
                                    {
                                        int mUserSeqNo = 0;
                                        string mStatusCode = line.Substring(0, 3);
                                        if (mStatusCode == "903")
                                            mUserSeqNo = Int32.Parse(line.Substring(31, 6));
                                        else if (mStatusCode == "901")
                                            mUserSeqNo = Int32.Parse(line.Substring(21, 6));
                                        else
                                            mUserSeqNo = 0;

                                        if (mUserSeqNo > 0)
                                        {
                                            using (var uow = new UnitOfWork())
                                            {
                                                try
                                                {
                                                    // ABSATransaction AbsaTrans = new XPQuery<ABSATransaction>(uow).FirstOrDefault(a => a.TransmissionNumber == mTransmissionNo && a.HomingAccountNumber == mHAN && a.UserSequenceNumber == mUserSeqNo && a.BankservRecordIdentifier == "10");
                                                    ABSATransaction AbsaTrans = new XPQuery<ABSATransaction>(uow).FirstOrDefault(a => a.TransmissionNumber == mTransmissionNo && a.UserSequenceNumber == mUserSeqNo && a.BankservRecordIdentifier == "10");
                                                    if (AbsaTrans != null)
                                                    {
                                                        if (mStatusCode == "903")  //---- for ACCEPTED
                                                        {
                                                            AbsaTrans.AbsaStatus = "ACCEPTED";
                                                            using (var result = new BackOfficeWebServiceClient("BackOfficeWebServer.NET"))
                                                            {
                                                                var accountId = Disbursement(AbsaTrans.ApplicationId);
                                                                var ffdisburse = UpdateFlowFinanceDisbursement(AbsaTrans.ApplicationId, "AOL" + accountId.ToString().PadLeft(7, '0'));
                                                                if (accountId > 0)
                                                                {
                                                                    fn_printAgree(AbsaTrans.ApplicationId);
                                                                    var StatusResult = result.UpdateApplicationDisbursementStatus("1=1", AbsaTrans.ApplicationId, (int)BackOfficeEnum.NewStatus.STATUS_DISBURSED);
                                                                }
                                                            }
                                                        }
                                                        else if (mStatusCode == "901")   //---- for REJECTED
                                                        {
                                                            AbsaTrans.AbsaStatus = "REJECTED";
                                                        }
                                                        AbsaTrans.Save();
                                                        uow.CommitChanges();
                                                    }
                                                }
                                                catch (Exception ex)
                                                {
                                                    log.Info(ex.ToString());
                                                    return Response<string>.CreateResponse(Constants.Failed, null, new ErrorHandler()
                                                    { ErrorCode = Constants.AbsaStatusUpdateErrorCode, Message = Constants.AbsaStatusUpdateErrorMessage });
                                                }
                                            }

                                        }
                                    }
                                }
                            }

                            //---- to move completed file to Archive folder
                            string sourceFile = Path.Combine(AbsaReplyFilePath, mFilename);
                            string destFile = Path.Combine(AbsaReplyFileArchivePath, mFilename);
                            if (!Directory.Exists(AbsaReplyFileArchivePath))
                            {
                                Directory.CreateDirectory(AbsaReplyFileArchivePath);
                            }
                            File.Move(sourceFile, destFile);
                        }
                        catch (Exception ex)
                        {
                            log.Info(ex.ToString());
                            return Response<string>.CreateResponse(Constants.Failed, null, new ErrorHandler()
                            { ErrorCode = Constants.ApplicationErrorCode, Message = "Error updating ABSA Reply status" });
                        }
                    }
                    return Response<string>.CreateResponse(Constants.Success, "ABSA reply status update process completed", null);
                }
                else
                {
                    log.Info("Files not found...!");
                    return Response<string>.CreateResponse(Constants.Failed, "Files not found...!", null);
                }
            }
            catch (Exception ex)
            {
                log.Info(ex.ToString());
                return Response<string>.CreateResponse(Constants.Failed, null, new ErrorHandler()
                { ErrorCode = Constants.ApplicationErrorCode, Message = Constants.ApplicationDetailsNotFound });
            }
        }


        // To read TransmissionNo + StatusCode + HAN + UserSeqNo from Transaction record
        // of ABSA reply file and Concate both and then return
        //private static String GetStatusHANFromAbsaFile(String fileName, string filepath)
        //{
        //    fileName = filepath + fileName;
        //    string[] lineText = File.ReadAllLines(fileName);
        //    //----   TransmissionNo + StatusCode + HAN + UserSeqNo
        //    var StatusHAN = lineText[1].Substring(28, 7) + lineText[3].Substring(0, 3) + lineText[3].Substring(44, 11) + lineText[3].Substring(31, 6);
        //    return StatusHAN;
        //}


        //ABSA Scheduler method 
        [HttpPost]
        [Route("Applications/ProcessABSA")]
        public void CallAbsaScheduler()
        {
            Console.ForegroundColor = ConsoleColor.Green;

            Console.WriteLine("Finding pending records and Creating ABSA Input batch file...");
            CreateABSABatchFile();

            // if (Boolean.Parse(ConfigurationManager.AppSettings["isTestEnvironment"])) 
            if (Boolean.Parse(ConfigurationManager.AppSettings["MockABSAReply"]))
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("Creating dummy REPLY file...");
                CreateABSAReplyFile();
            }

            Console.WriteLine("Reading ABSA reply file and updating ABSA Status...");
            UpdateAbsaStatus();

            Console.ResetColor();
        }

        // ASBA reply file generator for Testing purpose
        public void CreateABSAReplyFile()
        {
            string AbsaReplySampleFile = HttpContext.Current.Server.MapPath($"~/Content/ABSA/ReplySample/AbsaReplySample.txt");
            string InputFilePath = ConfigurationManager.AppSettings["AbsaINPUTPath"];
            string ReplyFilePath = ConfigurationManager.AppSettings["AbsaREPLYPath"];
            string InputArchiveFilesPath = ConfigurationManager.AppSettings["AbsaINPUT_ARCHIVEPath"];

            string recordIdentifier;
            string dataSetStatus;
            using (var uow = new UnitOfWork())
            {
                ABSAMaster aBSAMaster = new XPQuery<ABSAMaster>(uow).FirstOrDefault();
                recordIdentifier = aBSAMaster.RecordIdentifier;
                dataSetStatus = aBSAMaster.DataSetStatus;
            }

            var di = new DirectoryInfo(InputFilePath);
            var fileInfos = di.EnumerateFiles("INPUT.*", SearchOption.TopDirectoryOnly).OrderBy(n => n.CreationTime);
            if (fileInfos != null)
            {
                string readFileName = "";
                string writeFileName = "";
                string mFinalString = "";
                foreach (FileInfo fileInfo in fileInfos)
                {
                    readFileName = fileInfo.Name;
                    writeFileName = "REPLY." + DateTime.Now.ToString("MMddhhmmss");
                    string readFile = InputFilePath + readFileName;
                    string writeFile = ReplyFilePath + writeFileName;
                    string[] AbsaReplySampleFileLines = File.ReadAllLines(AbsaReplySampleFile);
                    string[] readFileLines = File.ReadAllLines(readFile);
                    string line1 = AbsaReplySampleFileLines[0];
                    string line2 = AbsaReplySampleFileLines[1].Substring(0, 27) + readFileLines[0].Substring(47, 7) + AbsaReplySampleFileLines[1].Substring(34);
                    string line3 = AbsaReplySampleFileLines[2].Substring(0, 26) + readFileLines[0].Substring(47, 7) + AbsaReplySampleFileLines[1].Substring(33);
                    mFinalString = line1 + "\r\n" + line2 + "\r\n" + line3;
                    foreach (var readLine in readFileLines)
                    {
                        string transLine = "";
                        // if (readLine.Substring(0, 3) == "060" && readLine.Substring(4, 2) == "10")
                        if (readLine.Substring(0, 3) == recordIdentifier && readLine.Substring(4, 2) == "10")
                        {
                            // transLine = "903T" + readLine.Substring(0, 194);
                            transLine = "903" + dataSetStatus + readLine.Substring(0, 194);

                            //if (ConfigurationManager.AppSettings["isTestEnvironment"] == "true")
                            //{
                            //    transLine = "903T" + readLine.Substring(0, 194);
                            //}
                            //else
                            //{
                            //    transLine = "903L" + readLine.Substring(0, 194);
                            //}

                            mFinalString = mFinalString + "\r\n" + transLine;
                        }
                    }
                    string lineContra = AbsaReplySampleFileLines[4];
                    string lineFooter = AbsaReplySampleFileLines[5];
                    mFinalString = mFinalString + "\r\n" + lineContra + "\r\n" + lineFooter;
                    File.WriteAllText(writeFile, mFinalString);
                    //---- to move completed file to Archive folder
                    string sourceFile = Path.Combine(InputFilePath, readFileName);
                    string destFile = Path.Combine(InputArchiveFilesPath, readFileName);
                    if (!Directory.Exists(InputArchiveFilesPath))
                    {
                        Directory.CreateDirectory(InputArchiveFilesPath);
                    }
                    File.Move(sourceFile, destFile);
                }
            }
        }

        public void ApproveDebicheckDebitOrder(int ApplicationId)
        {
            log.Info("Updating Debit Order Status, Application : " + ApplicationId);
            using (var uow = new UnitOfWork())
            {
                var mandate = new XPQuery<DebiCheckMandate>(uow).FirstOrDefault(a => a.ApplicationId == ApplicationId);
                mandate.Status = "APPROVED";
                mandate.Save();
                uow.CommitChanges();

                using (var result = new BackOfficeWebServiceClient("BackOfficeWebServer.NET"))
                {
                    var filterConditions = "1=1";
                    var StatusResult = result.UpdateApplicationDisbursementStatus(filterConditions, Convert.ToInt32(mandate.ContractNumber), (int)BackOfficeEnum.NewStatus.STATUS_DEBIT_ORDER_APPROVED);
                }
            }

            DBManager.UpdateApplicationCMSState(ApplicationId, BackOfficeEnum.Action.APPLICATION_APPROVE_DEBIT_ORDER.ToString());
        }
        [HttpPost]
        [Route("Applications/MockDebicheckRepayment/{ApplicationId}")]
        public void MockDebicheckRepayment(int ApplicationId) {
            DebiCheckIntegration ob = new DebiCheckIntegration();
            DebiCheckMandate debiCheckMandate=null ;
            using (var uow = new UnitOfWork())
            {
                var mandate = new XPQuery<DebiCheckMandate>(uow).FirstOrDefault(a => a.ApplicationId == ApplicationId);
                debiCheckMandate = mandate;
                ob.MockDebicheckRepayment(debiCheckMandate,DateTime.Now,DateTime.Now,true,"");
                
            }
        }

    }
}
Added one line at the end of this file in MainRepo
