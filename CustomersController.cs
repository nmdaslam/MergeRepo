using Atlas.Domain.Model;
using DevExpress.Xpo;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Http;
using Newtonsoft.Json;
using Atlas.Online.Data.Models.Definitions;
using Atlas.Online.Data.Models.DTO;
using BackOfficeServer.Common;
using FrameworkLibrary.ResponseBase;
using FrameworkLibrary.ExceptionBase;
using Atlas.Common.Common;
using BackOfficeServer.BackOfficeWebServer;
using BackOfficeServer.OrchestrationService;
using BackOfficeServer.ViewModels;
using AutoMapper;
using Atlas.Online.Data.Models.Dto;
using Atlas.Common.Utils;
using System.Web;
using Atlas.Domain.DTO.BOS;
using static Atlas.Common.Utils.BackOfficeEnum;
using Atlas.Domain.DTO.Account;
using BackOfficeServer.BackOfficeAccountServer;
using System.Linq.Dynamic;
using Npgsql;
using log4net;
using System.Net.Http.Headers;
using System.Net.Http;
using System.IO;
using System.Net;
using FrameworkLibrary.Common;
using BackOfficeServer.Integrations;
using DevExpress.Xpo.Metadata;

namespace BackOfficeServer.Controllers
{
    public class CustomersController : BaseController
    {
        private static readonly ILog log = LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        //[OutputCache(60)]
        /// <summary>
        /// Returns customer details with related populated fields.
        /// </summary>
        /// <param name="id">id of the customer</param>
        /// <returns></returns>
        [HttpGet]
        [Route("Customers/{id}")]
        public Response<VMCustomerDetails> GetCustomerById(int id)
        {
            try
            {
                var role = HasAccess(BO_Object.Customers, BackOfficeEnum.Action.VIEW);
                if (role.Status == Constants.Success)
                {
                    log.Info(string.Format("Get customer {0} details", id));
                    return CustomerDetails(id);
                }
                else
                {
                    log.Info(string.Format("Authentication failed for getting customer {0} details. Error: {1}", id, role.Error));
                    return Response<VMCustomerDetails>.CreateResponse(Constants.Failed, null, role.Error);
                }
            }
            catch (Exception ex)
            {
                log.Error(string.Format("Error getting customer {0} details\nError: {1}", id, ex));
                return Response<VMCustomerDetails>.CreateResponse(Constants.Failed, null, new ErrorHandler() { ErrorCode = Constants.CustomerErrorCode, Message = Constants.CustomerDetailsErrorMessage });
            }
        }
        
        /// <summary>
        /// It will return all the actions that can be performed on the given customer
        /// </summary>
        /// <param name="id">id of the customer</param>
        /// <returns></returns>
        [HttpGet]
        [Route("Customers/{id}/Actions")]
        public Response<VMCustomerActionList> Action(long id)
        {
            string lastStatus = string.Empty;
            long? personId = 0;
            VMCustomerActionList lstActions = new VMCustomerActionList();
            try
            {
                log.Info(string.Format("Get actions for customer {0}", id));
                var role = this.HasAccess(BO_Object.Customers, BackOfficeEnum.Action.VIEW);
                if (role.Status == Constants.Success)
                {
                    try
                    {
                        log.Info(string.Format("Getting person id for customer {0}", id));
                        personId = GetPersonInfo((int)id, out lastStatus);
                    }
                    catch (Exception ex)
                    {
                        log.Error(string.Format("Error getting person id for customer {0}\nError: {1}", id, ex));
                        throw;
                    }

                    long roleId = Convert.ToInt64(HttpContext.Current.Session["RoleId"]);

                    var filterConditions = FilterUtil.GetFiltersAsString(BO_Object.Customers, Convert.ToInt32(HttpContext.Current.Session["UserId"]));

                    log.Info(string.Format("Getting allowed actios for customer {0}", id));
                    var actions = ActionUtil.GetAllowedActions(BO_Object.Customers, id, filterConditions);

                    using (var uow = new UnitOfWork())
                    {
                        try
                        {
                            log.Info(string.Format("check role api access for customer {0} actions", id));
                            var act = new XPQuery<BOS_RoleApiAccess>(uow).Where(r => r.Role.RoleId == roleId).Select(a => a.Action.ActionId).ToList();
                            actions = actions.Where(a => act.Contains(a.ActionId)).ToList();
                        }
                        catch (Exception ex)
                        {
                            log.Error(string.Format("Error checking role api access for customer {0} actions\nError: {1}", id, ex));
                            throw;
                        }
                    }

                    if (actions != null && actions.Count() > 0)
                    {
                        using (var result = new BackOfficeWebServiceClient("BackOfficeWebServer.NET"))
                        {
                            try
                            {
                                log.Info(string.Format("get sub objects status for customer {0}", id));
                                var subObjs = result.GetCustomerCategoryStatus((int)id);
                                var statusId = subObjs.FirstOrDefault(s => s.CustomerObject == BO_ObjectAPI.master)==null?0: subObjs.FirstOrDefault(s => s.CustomerObject == BO_ObjectAPI.master).NewStatusId;

                                using (var uow = new UnitOfWork())
                                {
                                    log.Info(string.Format("Get master actions for customer {0}", id));
                                    foreach (var mstrAction in actions)
                                    {
                                        try
                                        {
                                            if (mstrAction.API.ToLower().Contains(BO_ObjectAPI.master.ToString().Trim().ToLower()))
                                            {
                                                bool IsAllStatusMatched = true;
                                                int clientid = Convert.ToInt32(id);
                                                IsAllStatusMatched = result.IsStatusMatchedforallObjects(statusId, clientid);
                                                if (!IsAllStatusMatched)
                                                {
                                                    actions = actions.Where(c => !(c.API.Trim().ToLower().Contains(BO_ObjectAPI.master.ToString().Trim().ToLower()))).ToList();
                                                }
                                            }
                                        }
                                        catch (Exception ex)
                                        {
                                            log.Error(string.Format("Error getting master actions for customer {0}, action name {1}\nError: {1}", id, mstrAction.Name, ex));
                                            throw;
                                        }
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                log.Error(string.Format("Error getting sub objects status for customer {0}\nError: {1}", id, ex));
                                throw;
                            }
                        }
                    }
                    lstActions = new VMCustomerActionList { ClientId = id, Actions = actions };

                    return Response<VMCustomerActionList>.CreateResponse(Constants.Success, lstActions, null);
                }
                else
                {
                    log.Info(string.Format("Authentication failed for getting customer {0} actions. Error: {1}", id, role.Error));
                    return Response<VMCustomerActionList>.CreateResponse(Constants.Failed, null, role.Error);
                }
            }

            catch (Exception ex)
            {
                log.Error(string.Format("Error getting customer {0} actions. Error: {1}", id, ex));
                return Response<VMCustomerActionList>.CreateResponse(Constants.Failed, null, new ErrorHandler() { ErrorCode = Constants.CustomerErrorCode, Message = Constants.ActionsErrorMessage });
            }
        }

        [NonAction]
        internal Response<VMCustomerDetails> CustomerDetails(int id)
        {
            string lastStatus = string.Empty;
            long? personId = 0;
            var clientDto = new ClientDto();
            var categoryList = new List<VMCustomerCategory>();
            var loan = new List<LoanSummaryDto>();
            var historyDto = new List<BOS_CustomerHistoryDTO>();
            var lstDocument = new List<DocumentsDto>();
            PER_PersonDTO[] personDTO;
            CustomerCategoryStatusDto[] subObjs;

            try
            {
                log.Info(string.Format("Get customer {0} details", id));
                var filterConditions = FilterUtil.GetFiltersAsString(BO_Object.Customers, Convert.ToInt32(HttpContext.Current.Session["UserId"]));
                bool filterCheck = false;
                using (var web = new BackOfficeWebServiceClient("BackOfficeWebServer.NET"))
                {
                    try
                    {
                        log.Info(string.Format("check filters for customer {0}", id));
                        filterCheck = web.CheckCutomerFilter(id, filterConditions);
                    }
                    catch (Exception ex)
                    {
                        log.Error(string.Format("Error checking filter conditions for customer {0}\nError: {1}", id, ex));
                        throw;
                    }
                }
                if (filterCheck)
                {
                    using (var web = new BackOfficeWebServiceClient("BackOfficeWebServer.NET"))
                    {
                        try
                        {
                            log.Info(string.Format("get loan details for customer {0}", id));
                            loan = web.GetLoanDetails(id).ToList();
                        }
                        catch (Exception ex)
                        {
                            log.Error(string.Format("Error getting loan details for customer {0}\nError: {1}", id, ex));
                            throw;
                        }

                        try
                        {
                            log.Info(string.Format("get client details for customer {0}", id));
                            clientDto = web.GetCustomerDetails(id, filterConditions);
                        }
                        catch (Exception ex)
                        {
                            log.Error(string.Format("Error getting client details for customer {0}\nError: {1}", id, ex));
                            throw;
                        }

                        try
                        {
                            log.Info(string.Format("get documents for customer {0}", id));
                            lstDocument = web.GetFiles(id, 0, (HttpContext.Current.Request.Url.Authority + HttpContext.Current.Request.ApplicationPath)).ToList();
                        }
                        catch (Exception ex)
                        {
                            log.Error(string.Format("Error getting documents for customer {0}\nError: {1}", id, ex));
                            throw;
                        }

                        try
                        {
                            log.Info(string.Format("get person details for customer {0}, client {1}", id, clientDto.ClientId));
                            personDTO = web.GetApplicationDetailByClientId(clientDto.ClientId);
                        }
                        catch (Exception ex)
                        {
                            log.Error(string.Format("Error getting person details for customer {0}\nError: {1}", id, ex));
                            throw;
                        }

                        try
                        {
                            log.Info(string.Format("get sub objects status for customer {0}", id));
                            subObjs = web.GetCustomerCategoryStatus(id);
                        }
                        catch (Exception ex)
                        {
                            log.Error(string.Format("Error getting sub object status for customer {0}\nError: {1}", id, ex));
                            throw;
                        }
                    }
                    personId = GetPersonInfo(id, out lastStatus);

                    //TODO: This will add in OrchestrationService. need to map with person id later.
                    using (var uow = new UnitOfWork())
                    {
                        try
                        {
                            log.Info(string.Format("get customer {0} history", id));
                            var history = new XPQuery<BOS_CustomerHistory>(uow).Where(x => x.ClientId == id).ToList();
                            foreach (var h in history)
                            {
                                historyDto.Add(Mapper.Map<BOS_CustomerHistory, BOS_CustomerHistoryDTO>(h));
                            }
                        }
                        catch (Exception ex)
                        {
                            log.Error(string.Format("Error getting event history for customer {0}\nError: {1}", id, ex));
                            throw;
                        }

                        if (lstDocument != null && lstDocument.Count() > 0)
                        {
                            log.Info(string.Format("update document information for customer {0}", id));
                            foreach (var item in lstDocument)
                            {
                                try
                                {
                                    item.UploadedBy = new XPQuery<Auth_User>(uow).Where(c => c.UserId == item.ModifiedBy).Select(s => s.UserName).FirstOrDefault();
                                }
                                catch (Exception ex)
                                {
                                    log.Error(string.Format("Error updating documentId {0} information for customer {1}\nError: {2}", item.DocumentId, id, ex));
                                    throw;
                                }
                            }
                        }
                    }

                    var profile = new VMClient() { Client = clientDto };
                    var masterActions = new List<BOS_ActionDTO>();
                    var statusId = subObjs.FirstOrDefault(s => s.CustomerObject == BO_ObjectAPI.master).NewStatusId;
                    try
                    {
                        log.Info(string.Format("get master actions for customer {0}", id));
                        masterActions = ActionUtil.GetAllowedActions(BO_Object.Customers, BO_ObjectAPI.master, statusId, id);
                    }
                    catch (Exception ex)
                    {
                        log.Error(string.Format("Error getting master actions for customer {0}\nError: {1}", id, ex));
                        throw;
                    }
                    using (UnitOfWork uow = new UnitOfWork())
                    {
                        if (masterActions != null && masterActions.Count() > 0)
                        {
                            log.Info(string.Format("get approve/verify master actions for customer {0}", id));
                            foreach (var mstrAction in masterActions)
                            {
                                using (var result = new BackOfficeWebServiceClient("BackOfficeWebServer.NET"))
                                {
                                    try
                                    {
                                        bool IsAllStatusMatched = true;
                                        int clientid = Convert.ToInt32(id);
                                        IsAllStatusMatched = result.IsStatusMatchedforallObjects(statusId, clientid);
                                        if (!IsAllStatusMatched)
                                        {
                                            masterActions = masterActions.Where(c => !(c.API.Trim().ToLower().Contains(BO_ObjectAPI.master.ToString().Trim().ToLower()))).ToList();
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        log.Error(string.Format("Error getting master action {0} for customer {1}\nError: {2}", mstrAction.ActionName, id, ex));
                                        throw;
                                    }
                                }
                            }
                        }
                    }
                    var checklist = GetCheckList(id);
                    try
                    {
                       
                        log.Info(string.Format("get all subobject details for customer {0}", id));
                        categoryList = (from n in personDTO
                                        select new VMCustomerCategory
                                        {
                                            BankDetails = GetBankDetails(n.BankDetails, subObjs.FirstOrDefault(s => s.CustomerObject == BO_ObjectAPI.bankdetails), id,checklist).FirstOrDefault(),
                                            LoanSummary = loan,
                                            Employer = GetEmployerDetails(n.Employer, subObjs.FirstOrDefault(s => s.CustomerObject == BO_ObjectAPI.employerdetails), id),
                                            Profile = GetProfileDetails(profile, n.AddressDetails, subObjs.FirstOrDefault(s => s.CustomerObject == BO_ObjectAPI.profile), id),
                                            // IncomeExpenses = GetIncomeExpenseDetails(subObjs.FirstOrDefault(s => s.CustomerObject == BO_ObjectAPI.incomeexpense), id),
                                            Documents = lstDocument
                                        }).ToList();
                        using (UnitOfWork uow = new UnitOfWork())
                        {
                            historyDto = (from h in historyDto
                                          select new BOS_CustomerHistoryDTO
                                          {
                                              Action = new BOS_ActionDTO { ActionId = h.Action.ActionId, ActionName = h.Action.ActionName, API = string.Format("{0}/{1}/{2}", BO_Object.Customers, h.ClientId, h.Action.ActionName) },
                                              Role = h.Role,
                                              User = h.User,
                                              ClientId = h.ClientId,
                                              ActionDate = h.ActionDate,
                                              Comment = h.Comment,
                                              CustomerHistoryId = h.CustomerHistoryId,
                                              FieldsModified = h.FieldsModified,
                                              Category = h.Category,
                                              DisplayName = Helper.GetCategoryDisplayName((BO_ObjectAPI)Enum.Parse(typeof(BO_ObjectAPI), h.Category, true)).Replace("Master", "Customer"),
                                              Version = h.Version
                                          }).OrderByDescending(h => h.CustomerHistoryId).ToList();
                        }
                    }
                    catch (Exception ex)
                    {
                        log.Error(string.Format("Error getting all subobject details for customer {0}\nError: {1}", id, ex));
                        throw;
                    }
                    try
                    {
                        log.Info(string.Format("get details for customer {0}", id));
                       
                        var customer = new VMCustomerDetails()
                        {
                            CategoryList = categoryList,
                            Actions = masterActions,
                            ClientId = id,
                            EventHistory = historyDto,
                            CheckList = checklist,
                            ClientAccounts = GetCustomerAccounts(id),
                            ClientApplications = ClientApplications(id)
                        };
                        return Response<VMCustomerDetails>.CreateResponse(Constants.Success, customer, null);
                    }
                    catch (Exception ex)
                    {
                        log.Error(string.Format("Error getting details for customer {0}\nError: {1}", id, ex));
                        throw;
                    }
                }
                else
                {
                    log.Info(string.Format("Filter conditions failed for customer {0}", id));
                    return Response<VMCustomerDetails>.CreateResponse(Constants.Failed, null, new ErrorHandler() { ErrorCode = Constants.CustomerErrorCode, Message = Constants.FiltersErrorMessage });
                }
            }
            catch (Exception ex)
            {
                log.Error(string.Format("Error getting customer {0} details\nError: {1}", id, ex));
                throw;
            }
        }

        private List<BOS_ClientChecklistDto> GetCheckList(int clientId)
        {
            try
            {
                using (var web = new BackOfficeWebServiceClient("BackOfficeWebServer.NET"))
                {
                    log.Info(string.Format("Get checklist for client {0}", clientId));
                    var checklist = web.GetClientChecklist(clientId)?.ToList();
                    if (checklist != null)
                        checklist.ForEach(h => h.ClientChecklistMaster.DisplayName = Helper.GetCategoryDisplayName((BO_ObjectAPI)Enum.Parse(typeof(BO_ObjectAPI), h.ClientChecklistMaster.SubObject, true)).Replace("Master", "Customer"));
                    return checklist;
                }
            }
            catch (Exception ex)
            {
                log.Error(string.Format("Error getting checklist for client {0}\nError: {1}", clientId, ex));
                throw;
            }
        }

        private EmployerDTO GetEmployerDetails(CPY_CompanyDTO company, CustomerCategoryStatusDto category, int id)
        {
            try
            {
                log.Info(string.Format("Get employer details for client {0}", id));
                EmployerDTO emp = new EmployerDTO();
                if (company.Addresses?.Count > 0)
                {
                    emp.Address = new AddressDto();
                    emp.Address.AddressId = (int)company.Addresses[0].AddressId;
                    emp.Address.AddressLine1 = company.Addresses[0].Line1;
                    emp.Address.AddressLine2 = company.Addresses[0].Line2;
                    emp.Address.AddressLine3 = company.Addresses[0].Line3;
                    emp.Address.AddressLine4 = company.Addresses[0].Line4;
                    emp.Address.Province = company.Addresses[0].Province != null ? new ProvinceDto() { ProvinceId = (int)company.Addresses[0].Province.ProvinceId } : null;
                    emp.Address.PostalCode = company.Addresses[0].PostalCode;
                }
                //emp.ContactNo = company.Contacts?[0]?.Value;
                emp.ContactNo = company.ContactNo;
                emp.EmployerId = (int)company.CompanyId;
                emp.Name = company.Name;
                emp.Industry = new IndustryDTO() { IndustryId = company.IndustryId };
                emp.EmployerCode = company.EmployerCode;
                emp.ContactNo = company.ContactNo;
                emp.SupervisorName = company.SupervisorName;
                emp.NetPay = company.NetPay;
                emp.SalaryType = company.SalaryType;
                emp.TelephoneNo = company.TelephoneNo;
                emp.CellNo = company.CellNo;
                emp.OccupationCode = company.OccupationCode;
                emp.StartDate = company.StartDate;
                emp.EmployeeNo = company.EmployeeNo;
                emp.PayDay = company.PayDay;
                emp.HistoryUrl = Helper.GetHistoryUrl(BO_Object.Customers, BO_ObjectAPI.employerdetails, id);

                try
                {
                    log.Info(string.Format("Get employer actions for client {0}", id));
                    if (category == null)
                        emp.Actions = ActionUtil.GetAllowedActions(BO_Object.Customers, BO_ObjectAPI.employerdetails, 0, id);
                    else
                        emp.Actions = ActionUtil.GetAllowedActions(BO_Object.Customers, BO_ObjectAPI.employerdetails, category.NewStatusId, id);
                }
                catch (Exception ex)
                {
                    log.Error(string.Format("Error getting employer actions for client {0}\nError: {1}", id, ex));
                    throw;
                }
                return emp;
            }
            catch (Exception ex)
            {
                log.Error(string.Format("Error getting employer details for client {0}\nError: {1}", id, ex));
                throw;
            }
        }

        private VMClient GetProfileDetails(VMClient profile, List<AddressDTO> addressDetails, CustomerCategoryStatusDto category, int id)
        {
            try
            {
                log.Info(string.Format("Get profile details for client {0}", id));
                if (addressDetails?.Count > 0)
                {
                    var address = addressDetails[0];
                    profile.Address = new AddressDto();
                    profile.Address.AddressType = new AddressTypeDto();
                    profile.Address.Province = new ProvinceDto();
                    if (address != null)
                    {
                        log.Info(string.Format("Get address details for client {0}", id));
                        profile.Address.AddressId = (int)address.AddressId;
                        profile.Address.AddressLine1 = address.Line1;
                        profile.Address.AddressLine2 = address.Line2;
                        profile.Address.AddressLine3 = address.Line3;
                        profile.Address.AddressLine4 = address.Line4;
                        if (address.AddressType != null)
                        {
                            log.Info(string.Format("Get address type details for client {0}", id));
                            profile.Address.AddressType.AddressTypeId = address.AddressType.AddressTypeId;
                            profile.Address.AddressType.Description = address.AddressType.Description;
                            profile.Address.AddressType.Type = address.AddressType.Type;

                        }
                        profile.Address.PostalCode = address.PostalCode;
                        if (address.Province != null)
                        {
                            log.Info(string.Format("Get province details for client {0}", id));
                            profile.Address.Province.Description = address.Province.Description;
                            profile.Address.Province.ProvinceId = (int)address.Province.ProvinceId;
                            profile.Address.Province.Type = address.Province.Type;
                        }
                    }
                }
                try
                {
                    if (category == null)
                        profile.Actions = ActionUtil.GetAllowedActions(BO_Object.Customers, BO_ObjectAPI.profile, 0, id);
                    else
                        profile.Actions = ActionUtil.GetAllowedActions(BO_Object.Customers, BO_ObjectAPI.profile, category.NewStatusId, id);
                    profile.HistoryUrl = Helper.GetHistoryUrl(BO_Object.Customers, BO_ObjectAPI.profile, id);
                }
                catch (Exception ex)
                {
                    log.Error(string.Format("Error getting profile actions for client {0}\nError: {1}", id, ex));
                    throw;
                }
                return profile;
            }
            catch (Exception ex)
            {
                log.Error(string.Format("Error getting profile details for client {0}\nError: {1}", id, ex));
                throw;
            }
        }

        private List<BankDetailDto> GetBankDetails(List<BankDetailDTO> bankDetails, CustomerCategoryStatusDto category, int id, List<BOS_ClientChecklistDto> status)
        {
            try
            {
                log.Info(string.Format("getting bank details for client {0}", id));
                var lstBank = new List<BankDetailDto>();
                var bankd = new BankDetailDto();
                bankd.Bank = new BankDto();
                bankd.AccountType = new BankAccountTypeDto();
                if (bankDetails?.Count > 0)
                {
                    bankd.AccountName = bankDetails[0].AccountName;
                    bankd.AccountNo = bankDetails[0].AccountNum;
                    if (bankDetails[0].AccountType != null)
                    {
                        log.Info(string.Format("getting bank account type details for client {0}", id));
                        bankd.AccountType.AccountTypeId = (int)bankDetails[0].AccountType.AccountTypeId;
                        bankd.AccountType.Description = bankDetails[0].AccountType.Description;
                        bankd.AccountType.Type = bankDetails[0].AccountType.Type;
                    }
                    bankd.Bank = new BankDto();
                    bankd.Bank.BankId = bankDetails[0].Bank != null ? (int)bankDetails[0].Bank.BankId : 0;
                    bankd.Bank.Code = bankDetails[0].Bank != null ? bankDetails[0].Bank.UniversalCode : "";
                    bankd.Bank.Type = bankDetails[0].Bank == null ? new Atlas.Enumerators.General.BankName() : bankDetails[0].Bank.Type;
                    bankd.Bank.Description = bankDetails[0].Bank != null ? bankDetails[0].Bank.Description : "";
                    bankd.Bank.Code = bankDetails[0].Bank != null ? bankDetails[0].Bank.Code : "";
                    bankd.BankDetailId = (int)bankDetails[0].DetailId;
                    bankd.CreateDate = (DateTime)bankDetails[0].CreatedDT;
                    bankd.IsActive = bankDetails[0].IsActive;
                    bankd.CardNumber = bankDetails[0].CardNumber;
                    bankd.Period = new BankPeriodDto() { PeriodId = bankDetails[0].PeriodId };
                    bankd.HistoryUrl = Helper.GetHistoryUrl(BO_Object.Customers, BO_ObjectAPI.bankdetails, id);
                }
                try
                {
                    if (status != null)
                    {
                        log.Info(string.Format("getting bank actions for client {0}", id));
                        if (category == null)
                            bankd.Actions = ActionUtil.GetAllowedActions(BO_Object.Customers, BO_ObjectAPI.bankdetails, 0, id);
                        else
                        {
                           
                            bankd.Actions = ActionUtil.GetAllowedActions(BO_Object.Customers, BO_ObjectAPI.bankdetails, category.NewStatusId, id);


                            var _list = status.Where(x => x.ChecklistStatus.ChecklistStatusId == 2).ToList();
                            foreach (var remPers in _list)
                            {
                                string _act = remPers.ClientChecklistMaster.Check;
                                BOS_ActionDTO itemToRemove = new BOS_ActionDTO();
                                switch (_act)
                                {
                                    case "AVSCheck":
                                        itemToRemove = bankd.Actions.Find(x => x.ActionId ==(int)BackOfficeEnum.Action.AVS_CHECK);
                                        
                                        break;
                                    case "CDVCheck":
                                        itemToRemove = bankd.Actions.Find(x => x.ActionId == (int)BackOfficeEnum.Action.CHECK_DIGIT);

                                        break;

                                }
                                
                                bankd.Actions.Remove(itemToRemove);
                            };
                        }
                       
                    }
                }
                catch (Exception ex)
                {
                    log.Error(string.Format("Error getting bank actions for client {0}\nError: {1}", id, ex));
                    throw;
                }
                lstBank.Add(bankd);
                return lstBank;
            }
            catch (Exception ex)
            {
                log.Error(string.Format("Error getting bank details for client {0}\nError: {1}", id, ex));
                throw;
            }
        }
        
        /// <summary>
        /// it will return Last Status Action and PersonId in Postgres
        /// </summary>
        /// <param name="id"></param>
        /// <param name="LastStatus"></param>
        /// <returns></returns>
        private long? GetPersonInfo(int id, out string LastStatus)
        {
            try
            {
                log.Error(string.Format("Get person details for client {0}", id));
                long? personId = 0;
                using (var web = new BackOfficeWebServiceClient("BackOfficeWebServer.NET"))
                {
                    personId = web.GetPersonID(id);
                    LastStatus = web.GetCustomerStatus(id);
                }
                return personId;
            }
            catch (Exception ex)
            {
                log.Error(string.Format("Error getting person details for client {0}\nError: {1}", id, ex));
                throw;
            }
        }

        /// <summary>
        /// This method will initialize the list of customers for the given person details
        /// </summary>
        /// <param name="person">list of persons</param>
        /// <returns>list of customers</returns>
        private List<VMCustomer> GetCustomerList(ClientDto[] person, int page, int pageSize, out Pagination pagination)
        {
            try
            {
                log.Info(string.Format("Get client list"));

                List<VMCustomer> customerList = new List<VMCustomer>();
                pageSize = pageSize == 0 ? Convert.ToInt32(System.Configuration.ConfigurationManager.AppSettings["pageSize"]) : pageSize;
                page = page == 0 ? 1 : page;

                using (var uow = new UnitOfWork())
                {
                    log.Info(string.Format("Initialize client list"));
                    customerList = (from n in person
                                    select new VMCustomer
                                    {
                                        ClientId = n.ClientId,
                                        FirstName = n.Firstname,
                                        Surname = n.Surname,
                                        IDNumber = n.IDNumber,
                                        PhoneNumber = n.CellNo,
                                        Comment = new XPQuery<BOS_CustomerHistory>(uow).Where(x => x.ClientId == n.ClientId).OrderByDescending(x => x.CustomerHistoryId).FirstOrDefault() != null ? new XPQuery<BOS_CustomerHistory>(uow).Where(x => x.ClientId == n.ClientId).OrderByDescending(x => x.CustomerHistoryId).FirstOrDefault().Comment : "",
                                        DetailsUrl = "/Customers/" + n.ClientId,
                                        ActionsUrl = "/Customers/" + n.ClientId + "/Actions",

                                        Status = n.Description,
                                    }).OrderByDescending(c => c.ClientId).ToList();

                }
                pagination = new Pagination() { TotalNoOfData = customerList.Count(), PageNo = page, PageSize = pageSize };
                return customerList;
            }
            catch (Exception ex)
            {
                log.Error(string.Format("Error initializing client list\nError: {0}", ex));
                throw;
            }
        }

        /// <summary>
        /// This method creates new customer
        /// </summary>
        /// <param name="client">New customer to be created</param>
        /// <returns></returns>
        [HttpPost]
        [Route("customers/new")]
        public Response<long> CreateClient(VMClient client)
        {
            try
            {
                var role = this.HasAccess(BO_Object.Customers, BackOfficeEnum.Action.NEW);
                if (role.Status == Constants.Success)
                {
                    long customerId = 0;
                    ErrorHandler error = new ErrorHandler();
                    using (var uow = new UnitOfWork())
                    {
                        if (customerId == 0)
                        {
                            var filterConditions = FilterUtil.GetFiltersAsString(BO_Object.Customers, Convert.ToInt32(HttpContext.Current.Session["UserId"]));
                            client.Client.Description = GetdefaultStatus(BO_ObjectAPI.profile);
                            using (var result = new BackOfficeWebServiceClient("BackOfficeWebServer.NET"))
                            {
                                try
                                {
                                    log.Info(string.Format("Create new client"));
                                    customerId = result.CreateClient(client.Client, client.Address, filterConditions, out error);
                                }
                                catch (Exception ex)
                                {
                                    log.Error(string.Format("Error creating new client\nError: {0}", ex));
                                    throw;
                                }
                            }

                            //TODO: add to DB
                            if (customerId == 0)
                            {
                                log.Info(string.Format("Create new client failed."));
                                return Response<long>.CreateResponse(Constants.Failed, 0, error);
                            }
                            else
                            {
                                using (var result = new BackOfficeWebServiceClient("BackOfficeWebServer.NET"))
                                {
                                    try
                                    {
                                        log.Info(string.Format("Create new client {0} checklist.", customerId));
                                        result.CreatClientChecklist((int)customerId);
                                    }
                                    catch (Exception ex)
                                    {
                                        log.Error(string.Format("Error creating new client {0} checklist\nError: {1}", customerId, ex));
                                        throw;
                                    }
                                }
                                try
                                {
                                    log.Info(string.Format("Create history event for client {0}", customerId));
                                    VMEditedFields obj = new VMEditedFields() { FieldName = "Status", OldValue = null, NewValue = "EDITED" };
                                    var editedFields = JsonConvert.SerializeObject(obj);

                                    //update autometted comment and event history while creating client
                                    var history = new BOS_CustomerHistory(uow)
                                    {
                                        ClientId = Convert.ToInt32(customerId),
                                        User = new XPQuery<Auth_User>(uow).Where(u => u.UserId == Convert.ToInt32(HttpContext.Current.Session["UserId"])).FirstOrDefault(),
                                        Role = new XPQuery<BOS_Role>(uow).Where(u => u.RoleId == Convert.ToInt32(HttpContext.Current.Session["RoleId"])).FirstOrDefault(),
                                        Action = new XPQuery<BOS_Action>(uow).Where(u => u.ActionName.Trim().ToLower() == BackOfficeEnum.Action.NEW.ToString().Trim().ToLower()).FirstOrDefault(),
                                        ActionDate = DateTime.UtcNow,
                                        Comment = "Client Created Successfully",
                                        FieldsModified = editedFields,
                                        Category = BO_ObjectAPI.profile.ToString(),
                                        Version = (new XPQuery<BOS_CustomerHistory>(uow).Where(hi => hi.Category == BO_ObjectAPI.profile.ToString() && hi.ClientId == Convert.ToInt32(customerId)).Count() + 1)
                                    };
                                    history.Save();
                                    uow.CommitChanges();

                                }
                                catch (Exception ex)
                                {
                                    log.Error(string.Format("Error creating history event for client {0}\nError: {1}", customerId, ex));
                                    throw;
                                }

                                using (var result = new BackOfficeWebServiceClient("BackOfficeWebServer.NET"))
                                {
                                    try
                                    {
                                        log.Info(string.Format("Save master status for client {0}", customerId));
                                        var statusId = new XPQuery<BOS_WorkFlowService>(uow).FirstOrDefault(s => s.Object.ObjectName.ToString().ToLower() == BO_Object.Customers.ToString().ToLower() && s.API.ToString().ToLower().Contains(BO_ObjectAPI.master.ToString().ToLower()) && s.Status == null)?.NewStatus?.StatusId;
                                        result.SaveCustomerMasterStatus((int)customerId, (int)statusId);
                                    }
                                    catch (Exception ex)
                                    {
                                        log.Error(string.Format("Error saving master status for client {0}\nError: {1}", customerId, ex));
                                        throw;
                                    }
                                }
                                log.Info(string.Format("Create new client success. client id: {0}", customerId));
                                return Response<long>.CreateResponse(Constants.Success, customerId, null);
                            }
                        }
                    }
                    log.Info(string.Format("Create new client failed"));
                    return Response<long>.CreateResponse(Constants.Failed, 0, new ErrorHandler() { ErrorCode = Constants.CustomerErrorCode, Message = Constants.CustomerCreateErrorMessage });
                }
                else
                {
                    log.Info(string.Format("Authenticaion failed. Error {0}", role.Error));
                    return Response<long>.CreateResponse(Constants.Failed, 0, role.Error);
                }
            }
            catch (Exception ex)
            {
                log.Error(string.Format("Error creating new client\nError: {0}", ex));
                return Response<long>.CreateResponse(Constants.Failed, 0, new ErrorHandler() { ErrorCode = Constants.CustomerErrorCode, Message = Constants.CustomerCreateErrorMessage });
            }
        }
        
        /// <summary>
        /// This AddClientBankDetails Method is used to Create/Edit the bank details of 
        /// clients.It takes the payload of BankDetailsDTO and clientid ,if bankdetails are
        /// present for that client then it gets update or else create the new bank details
        /// </summary>
        /// <param name="bankDetails">
        /// It is BankDetailDTO object contains Bank,AccountType,Period objects 
        /// and AccountNo and AccountName properties
        /// </param>
        /// <param name="id">
        /// It is a id contains clientid
        /// </param>
        /// <returns>
        /// It returns BankDetailsId which is been created while adding/updating
        /// </returns>
        public Response<string> AddClientBankDetails(BankDetailDto bankDetails, int id, int newstatusId)
        {
            string editedFields = string.Empty;
            try
            {
                log.Info(string.Format("Add bank details for client {0}", id));
                var role = HasAccess(BO_Object.Customers, BackOfficeEnum.Action.NEW);
                if (role.Status == Constants.Success)
                {
                    if (bankDetails != null)
                    {
                        using (var result = new BackOfficeWebServiceClient("BackOfficeWebServer.NET"))
                        {
                            var filterConditions = FilterUtil.GetFiltersAsString(BO_Object.BankDetails, Convert.ToInt32(HttpContext.Current.Session["UserId"]));

                            try
                            {
                                editedFields = result.AddClientBankInfo(bankDetails, id, filterConditions, newstatusId);
                            }
                            catch (Exception ex)
                            {
                                log.Error(string.Format("Error in [result.AddClientBankInfo] for client {0}", id, ex));
                                throw;
                            }

                            if (editedFields != null)
                            {
                                if (editedFields != Constants.AuthorizationFailedCode)
                                {
                                    log.Info("Bank details success.");
                                    return Response<string>.CreateResponse(Constants.Success, editedFields, null);
                                }
                                else
                                {
                                    log.Info("Bank details update failed. authentication failed.");
                                    return Response<string>.CreateResponse(Constants.Failed, null,
                                        new ErrorHandler() { ErrorCode = Constants.AuthorizationFailedCode, Message = Constants.AuthorizationFailed });
                                }
                            }
                            else
                            {
                                log.Info("Bank details update failed. edited fields empty.");
                                return Response<string>.CreateResponse(Constants.Failed, null, new ErrorHandler() { ErrorCode = Constants.CustomerErrorCode, Message = Constants.UpdateBankDetailsErrorMessage });
                            }
                        }
                    }
                    else
                        log.Info("Bank details payload empty.");

                    return Response<string>.CreateResponse(Constants.Failed, null, new ErrorHandler() { ErrorCode = Constants.CustomerErrorCode, Message = Constants.ProvideBankErrorMessage });
                }
                else
                {
                    log.Info(string.Format("[AddClientBankDetails] Authentication failed. Error {0}", role.Error));
                    return Response<string>.CreateResponse(Constants.Failed, null, role.Error);
                }
            }
            catch (Exception ex)
            {
                log.Error(string.Format("Error updating bank details for client {0}", id, ex));
                return Response<string>.CreateResponse(Constants.Failed, null, new ErrorHandler() { ErrorCode = Constants.CustomerErrorCode, Message = Constants.UpdateBankDetailsErrorMessage });
            }
        }

        [HttpPost]
        [Route("customers/{id}/edit/employerdetails")]
        public Response<string> AddUpdateEmployer(int id, EmployerDTO emp, int newStatusId)
        {
            string editedFields = string.Empty;
            try
            {
                if (emp != null)
                {
                    {
                        log.Info(string.Format("[AddUpdateEmployer] for client {0}", id));
                        ErrorHandler error = null;
                        using (var uow = new UnitOfWork())
                        {
                            using (var result = new BackOfficeWebServiceClient("BackOfficeWebServer.NET"))
                            {
                                var filterConditions = FilterUtil.GetFiltersAsString(BO_Object.EmployerDetails, Convert.ToInt32(HttpContext.Current.Session["UserId"]));

                                if (emp.EmployerId > 0)
                                {
                                    try
                                    {
                                        log.Info(string.Format("update employer details for client {0}, employer {1}", id, emp.EmployerId));
                                        var role = this.HasAccess(BO_Object.Customers, BackOfficeEnum.Action.EDIT);
                                        if (role.Status == Constants.Success)
                                        {
                                            editedFields = result.UpdateEmployer(emp, id, newStatusId, filterConditions, out error);
                                        }
                                        else
                                        {
                                            log.Info(string.Format("Authentication failed when updating employer details failed for client {0}, employer {1}, error: {2}", id, emp.EmployerId, role.Error));
                                            return Response<string>.CreateResponse(Constants.Failed, null, role.Error);
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        log.Error(string.Format("Error updating employer details for client {0}\nError: {1}", id, ex));
                                        error = new ErrorHandler() { ErrorCode = Constants.CustomerErrorCode, Message = Constants.UpdateEmployerErrorMessage };
                                    }
                                }
                                else
                                {
                                    try
                                    {
                                        log.Info(string.Format("Add employer details for client {0}", id));
                                        //add employer details
                                        var role = this.HasAccess(BO_Object.Customers, BackOfficeEnum.Action.NEW);
                                        if (role.Status == Constants.Success)
                                        {
                                            emp.Status = GetdefaultStatus(BO_ObjectAPI.employerdetails);
                                            result.AddEmployer(emp, id, filterConditions, out error);
                                        }
                                        else
                                        {
                                            log.Info(string.Format("Authentication failed when adding employer details failed for client {0}, error: {1}", id, role.Error));
                                            return Response<string>.CreateResponse(Constants.Failed, null, role.Error);
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        log.Error(string.Format("Error adding employer details for client {0}\nError: {1}", id, ex));
                                        error = new ErrorHandler() { ErrorCode = Constants.CustomerErrorCode, Message = Constants.UpdateEmployerErrorMessage };
                                    }
                                }
                                if (error == null)
                                {
                                    log.Info(string.Format("[AddUpdateEmployer] success for client {0}", id));
                                    return Response<string>.CreateResponse(Constants.Success, editedFields, null);
                                }
                                else
                                {
                                    log.Info(string.Format("[AddUpdateEmployer] failed for client {0}", id));
                                    return Response<string>.CreateResponse(Constants.Failed, null, error);
                                }

                            }
                        }
                    }
                }
                else
                {
                    log.Info(string.Format("[AddUpdateEmployer] failed for client {0}. Payload is null", id));
                    return Response<string>.CreateResponse(Constants.Failed, null, new ErrorHandler() { ErrorCode = Constants.CustomerErrorCode, Message = Constants.ProvideEmployerErrorMessage });
                }
            }
            catch (Exception ex)
            {
                log.Error(string.Format("Error add/update employer details for client {0}\nError: {1}", id, ex));
                return Response<string>.CreateResponse(Constants.Failed, null, new ErrorHandler() { ErrorCode = Constants.CustomerErrorCode, Message = Constants.UpdateEmployerErrorMessage });
            }
        }

        [HttpPost]
        [Route("Customers/{clientId}")]
        public Response<string> EditClient(int clientId, [FromBody] VMClient client)
        {
            string editedFields = string.Empty;
            ErrorHandler error = new ErrorHandler();
            try
            {
                log.Info(string.Format("Edit client {0}", clientId));
                var role = HasAccess(BO_Object.Customers, BackOfficeEnum.Action.EDIT);
                if (role.Status == Constants.Success)
                {
                    using (var uow = new UnitOfWork())
                    {
                        if (client != null)
                        {
                            using (var result = new BackOfficeWebServiceClient("BackOfficeWebServer.NET"))
                            {
                                var filterConditions = FilterUtil.GetFiltersAsString(BO_Object.Customers, Convert.ToInt32(HttpContext.Current.Session["UserId"]));
                                editedFields = result.EditClient(clientId, client, filterConditions, out error);
                            }

                            if (editedFields == null)
                            {
                                log.Info(string.Format("Edit client {0} failed. Error: {1}", clientId, error));
                                return Response<string>.CreateResponse(Constants.Failed, null, error);
                            }
                            else
                            {
                                if (editedFields == Constants.AuthorizationFailedCode)
                                {
                                    log.Info(string.Format("Edit client {0} failed. Error: {1}", clientId, error));
                                    return Response<string>.CreateResponse(Constants.Failed, null, error);
                                }
                                else
                                {
                                    log.Info(string.Format("Edit client {0} success", clientId));
                                    return Response<string>.CreateResponse(Constants.Success, editedFields, null);
                                }
                            }
                        }
                    }
                    log.Info(string.Format("Edit client {0} failed. Payload is null", clientId));
                    return Response<string>.CreateResponse(Constants.Failed, null, new ErrorHandler() { ErrorCode = Constants.CustomerErrorCode, Message = Constants.ProvideCustomer });
                }
                else
                {
                    log.Info(string.Format("Edit client {0} failed. Authentication failed {1}", clientId, role.Error));
                    return Response<string>.CreateResponse(Constants.Failed, null, role.Error);
                }
            }
            catch (Exception ex)
            {
                log.Error(string.Format("Error editing client {0}\nError: {1}", clientId, ex));
                return Response<string>.CreateResponse(Constants.Failed, null, new ErrorHandler() { ErrorCode = Constants.CustomerErrorCode, Message = Constants.UpdateClientErrorMessage });
            }
        }
        
        private string GetdefaultStatus(BO_ObjectAPI _obj)
        {
            try
            {
                using (var uow = new UnitOfWork())
                {
                    log.Error(string.Format("Get default status of obj {0}", _obj));
                    string defaultstatus = new XPQuery<BOS_WorkFlowService>(uow).Where(wf => wf.Status.ToString() == null && wf.Object.ObjectId == (int)BO_Object.Customers).Select(s => s.NewStatus.Description).FirstOrDefault();
                    return defaultstatus;
                }
            }
            catch (Exception ex)
            {
                log.Error(string.Format("Error getting default status of obj {0}\nError: {1}", _obj, ex));
                throw;
            }
        }

        /// <summary>
        /// This method will change the state of the customer object
        /// </summary>
        /// <param name="id">id of the customer object</param>
        /// <param name="newStatus">new status to be set on the customer</param>
        /// <param name="action">action performed on the customer</param>
        /// <param name="data">parameters, if any</param>
        /// <param name="editedFields">fields that were edited while performing the action</param>
        public void UpdateCustomerStatus(int id, int newStatus, string action, string data, string editedFields)
        {
            try
            {
                using (var uow = new UnitOfWork())
                {
                    using (var result = new BackOfficeWebServiceClient("BackOfficeWebServer.NET"))
                    {
                        bool res = false;
                        try
                        {
                            log.Info("get edited fields");
                            editedFields = getEditedFields(editedFields, id, newStatus, BO_ObjectAPI.profile);
                        }
                        catch (Exception ex)
                        {
                            log.Error(string.Format("Error in get edited fields \nError: {1}", id, ex));
                            throw;
                        }

                        try
                        {
                            log.Info("change customer status");
                            res = result.ChangeCustomerStatus(id, newStatus);
                        }
                        catch (Exception ex)
                        {
                            log.Error(string.Format("Error updating customer {0} status \nError: {1}", id, ex));
                            throw;
                        }

                        if (res)
                        {
                            try
                            {
                                log.Info("add to history");
                                var json = JsonConvert.DeserializeObject<VMCustomer>(data);
                                var history = new BOS_CustomerHistory(uow)
                                {
                                    ClientId = Convert.ToInt32(id),
                                    User = new XPQuery<Auth_User>(uow).Where(u => u.UserId == Convert.ToInt32(HttpContext.Current.Session["UserId"])).FirstOrDefault(),
                                    Role = new XPQuery<BOS_Role>(uow).Where(u => u.RoleId == Convert.ToInt32(HttpContext.Current.Session["RoleId"])).FirstOrDefault(),
                                    Action = new XPQuery<BOS_Action>(uow).Where(u => u.ActionName.ToLower() == action.ToLower()).FirstOrDefault(),
                                    ActionDate = DateTime.UtcNow,
                                    Comment = json.Comment,
                                    FieldsModified = editedFields,// (editedFields != string.Empty ? editedFields : string.Empty)
                                    Category = BO_ObjectAPI.profile.ToString(),
                                    Version = (new XPQuery<BOS_CustomerHistory>(uow).Where(hi => hi.Category == BO_ObjectAPI.profile.ToString() && hi.ClientId == Convert.ToInt32(id)).Count() + 1)
                                };
                                uow.CommitChanges();
                            }
                            catch (Exception ex)
                            {
                                log.Error(string.Format("Error adding history for customer {0}\nError: {1}", id, ex));
                                throw;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                log.Error(string.Format("Error updating customer {0} status\nError: {1}", id, ex));
                throw;
            }
        }

        public void UpdateClientBankDetails(long id, int newStatus, string action, string data, string editedFields)
        {
            try
            {
                using (var uow = new UnitOfWork())
                {
                    using (var result = new BackOfficeWebServiceClient("BackOfficeWebServer.NET"))
                    {
                        bool res = false;
                        int clientid = Convert.ToInt32(id);
                        try
                        {
                            log.Info("get edited fields");
                            if (!string.IsNullOrEmpty(editedFields) && editedFields != "[]")
                                editedFields = getEditedFields(editedFields, clientid, newStatus, BO_ObjectAPI.bankdetails);
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

                        try
                        {
                            log.Info("change customer bank status");
                            res = result.ChangeClientBankDetailsStatus(clientid, newStatus);
                        }
                        catch (Exception ex)
                        {
                            log.Error(string.Format("Error updating customer {0} bank status \nError: {1}", id, ex));
                            throw;
                        }

                        if (res)
                        {
                            try
                            {
                                log.Info("add to history");
                                var json = JsonConvert.DeserializeObject<VMCustomer>(data);
                                var history = new BOS_CustomerHistory(uow)
                                {
                                    ClientId = Convert.ToInt32(id),
                                    User = new XPQuery<Auth_User>(uow).Where(u => u.UserId == Convert.ToInt32(HttpContext.Current.Session["UserId"])).FirstOrDefault(),
                                    Role = new XPQuery<BOS_Role>(uow).Where(u => u.RoleId == Convert.ToInt32(HttpContext.Current.Session["RoleId"])).FirstOrDefault(),
                                    Action = new XPQuery<BOS_Action>(uow).Where(u => u.ActionName.ToLower() == action.ToLower()).FirstOrDefault(),
                                    ActionDate = DateTime.UtcNow,
                                    Comment = json.Comment,
                                    FieldsModified = /*(editedFields == null || editedFields == "") ? newStatus :*/ editedFields,
                                    Category = BO_ObjectAPI.bankdetails.ToString(),
                                    Version = (new XPQuery<BOS_CustomerHistory>(uow).Where(hi => hi.Category == BO_ObjectAPI.bankdetails.ToString() && hi.ClientId == Convert.ToInt32(id)).Count() + 1)
                                };
                                uow.CommitChanges();
                            }
                            catch (Exception ex)
                            {
                                log.Error(string.Format("Error adding bank history for customer {0}\nError: {1}", id, ex));
                                throw;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                log.Error(string.Format("Error updating customer {0} bank status\nError: {1}", id, ex));
                throw;
            }
        }
        
        public bool Authorise(int id, int newStatus)
        {
            try
            {
                using (var uow = new UnitOfWork())
                {
                    log.Info(string.Format("Authorise customer {0}", id));
                    using (var result = new BackOfficeWebServiceClient("BackOfficeWebServer.NET"))
                    {
                        var filterConditions = FilterUtil.GetFiltersAsString(BO_Object.Customers, Convert.ToInt32(HttpContext.Current.Session["UserId"]));
                        var res = result.ActivateCustomer(id, newStatus, filterConditions);
                        if (res)
                        {
                            return true;
                        }
                    }
                }
                return false;
            }
            catch (Exception ex)
            {
                log.Error(string.Format("Error in Authorise customer {0}\nError: {1}", id, ex));
                return false;
            }
        }

        public bool VerifyCustomer(int id, int newStatus)
        {
            try
            {
                using (var uow = new UnitOfWork())
                {
                    log.Info(string.Format("Verify customer {0}", id));
                    using (var result = new BackOfficeWebServiceClient("BackOfficeWebServer.NET"))
                    {
                        //var res = result.VerifyCustomer(id, newStatus);
                        var filterConditions = FilterUtil.GetFiltersAsString(BO_Object.Customers, Convert.ToInt32(HttpContext.Current.Session["UserId"]));
                        var res = result.VerifyCustomer(id, newStatus, filterConditions);
                        if (res)
                        {
                            return true;
                        }
                    }
                }
                return false;
            }
            catch (Exception ex)
            {
                log.Error(string.Format("Error in Verify customer {0}\nError: {1}", id, ex));
                return false;
            }
        }
        
        public void UpdateClientEmployerDetails(long id, int newStatus, string action, string data, string editedFields)
        {
            try
            {
                using (var uow = new UnitOfWork())
                {
                    using (var result = new BackOfficeWebServiceClient("BackOfficeWebServer.NET"))
                    {
                        bool res = false;
                        int clientid = Convert.ToInt32(id);
                        try
                        {
                            log.Info("get edited fields");
                            if (!string.IsNullOrEmpty(editedFields) && editedFields != "[]")
                                editedFields = getEditedFields(editedFields, clientid, newStatus, BO_ObjectAPI.employerdetails);
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

                        try
                        {
                            log.Info("change customer employer status");
                            res = result.ChangeClientEmployerStatus(clientid, newStatus);
                        }
                        catch (Exception ex)
                        {
                            log.Error(string.Format("Error updating customer {0} employer status \nError: {1}", id, ex));
                            throw;
                        }
                        if (res)
                        {
                            try
                            {
                                log.Info("add to history");
                                var json = JsonConvert.DeserializeObject<VMCustomer>(data);
                                var history = new BOS_CustomerHistory(uow)
                                {
                                    ClientId = Convert.ToInt32(id),
                                    User = new XPQuery<Auth_User>(uow).Where(u => u.UserId == Convert.ToInt32(HttpContext.Current.Session["UserId"])).FirstOrDefault(),
                                    Role = new XPQuery<BOS_Role>(uow).Where(u => u.RoleId == Convert.ToInt32(HttpContext.Current.Session["RoleId"])).FirstOrDefault(),
                                    Action = new XPQuery<BOS_Action>(uow).Where(u => u.ActionName.ToLower() == action.ToLower()).FirstOrDefault(),
                                    ActionDate = DateTime.UtcNow,
                                    Comment = json.Comment,
                                    FieldsModified = /*(editedFields == null || editedFields == "") ? newStatus :*/ editedFields,
                                    Category = BO_ObjectAPI.employerdetails.ToString(),
                                    Version = (new XPQuery<BOS_CustomerHistory>(uow).Where(hi => hi.Category == BO_ObjectAPI.employerdetails.ToString() && hi.ClientId == Convert.ToInt32(id)).Count() + 1)
                                };
                                uow.CommitChanges();
                            }
                            catch (Exception ex)
                            {
                                log.Error(string.Format("Error adding employer history for customer {0}\nError: {1}", id, ex));
                                throw;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                log.Error(string.Format("Error updating customer {0} employer status\nError: {1}", id, ex));
                throw;
            }
        }

        public bool RejectAuthorization(int id, int newStatus)
        {
            try
            {
                using (var uow = new UnitOfWork())
                {
                    log.Info(string.Format("Reject authorizartion customer {0}", id));
                    using (var result = new BackOfficeWebServiceClient("BackOfficeWebServer.NET"))
                    {
                        //var res = result.RejectAuthorization(id, newStatus);
                        var filterConditions = FilterUtil.GetFiltersAsString(BO_Object.Customers, Convert.ToInt32(HttpContext.Current.Session["UserId"]));
                        var res = result.RejectAuthorization(id, newStatus, filterConditions);
                        if (res)
                        {
                            return true;
                        }
                    }
                }
                return false;
            }
            catch (Exception ex)
            {
                log.Error(string.Format("Error in reject authorization customer {0}\nError: {1}", id, ex));
                return false;
            }
        }
        
        public bool UpdateCustomerEveHistForAuthorization(long clientid, string data, string status = null, string action = null)
        {
            //update comment and event history while authorizing
            try
            {
                using (var uow = new UnitOfWork())
                {
                    log.Info(string.Format("Update Authorize Event History for customer {0}", clientid));

                    var oldStatus = new XPQuery<BOS_WorkFlowService>(uow).Where(u => u.Action.ActionName.Trim().ToLower() == action.Trim().ToLower() && u.Object.ObjectId == (int)BO_Object.Customers).Select(s => s.Status.Description).FirstOrDefault();
                    VMEditedFields obj = new VMEditedFields() { FieldName = "Status", OldValue = oldStatus, NewValue = status };
                    var editedFields = JsonConvert.SerializeObject(obj);
                    var json = JsonConvert.DeserializeObject<VMCustomer>(data);
                    var history = new BOS_CustomerHistory(uow)
                    {
                        ClientId = Convert.ToInt32(clientid),
                        User = new XPQuery<Auth_User>(uow).Where(u => u.UserId == Convert.ToInt32(HttpContext.Current.Session["UserId"])).FirstOrDefault(),
                        Role = new XPQuery<BOS_Role>(uow).Where(u => u.RoleId == Convert.ToInt32(HttpContext.Current.Session["RoleId"])).FirstOrDefault(),
                        Action = new XPQuery<BOS_Action>(uow).Where(u => u.ActionName.Trim().ToLower() == action.Trim().ToLower()).FirstOrDefault(),
                        ActionDate = DateTime.UtcNow,
                        Comment = json.Comment,
                        FieldsModified = editedFields,
                        Category = BO_ObjectAPI.master.ToString(),
                        Version = (new XPQuery<BOS_CustomerHistory>(uow).Where(hi => hi.Category == BO_ObjectAPI.master.ToString() && hi.ClientId == Convert.ToInt32(clientid)).Count() + 1)
                    };
                    history.Save();
                    uow.CommitChanges();
                    return true;
                }
            }
            catch (Exception ex)
            {
                log.Error(string.Format("Error in Update Authorize Event History for customer {0}\nError: {1}", clientid, ex));
                return false;
            }
        }

        [HttpPost]
        [Route("customers/View/{ViewName}")]
        public Response<List<VMCustomer>> GetAllCustomersView(string viewName, Condition condition)
        {
            try
            {
                log.Info(string.Format("Get customers for view {0}", viewName));
                var role = this.HasAccess(BO_Object.Customers, BackOfficeEnum.Action.VIEW);
                List<VMCustomer> lstCustomers = new List<VMCustomer>();

                if (role.Status == Constants.Success)
                {
                    using (var result = new BackOfficeWebServiceClient("BackOfficeWebServer.NET"))
                    {
                        int branchId = Convert.ToInt32(HttpContext.Current.Session["BranchId"]);
                        List<ClientDto> person = DBManager.GetClientView(viewName, branchId, ref condition);

                        if (person == null)
                        {
                            log.Info(string.Format("Get customers for view {0} failed.", viewName));
                            return Response<List<VMCustomer>>.CreateResponse(Constants.Failed, null, new ErrorHandler() { ErrorCode = Constants.CustomerErrorCode, Message = Constants.CustomerDetailsNotFound });
                        }
                        else
                        {
                            lstCustomers = GetCustomerList(person.ToArray(), condition.pagination.PageNo, condition.pagination.PageSize, out Pagination _pagination);
                            log.Info(string.Format("Get customers for view {0} success. Count {1}", viewName, lstCustomers.Count()));
                            return Response<List<VMCustomer>>.CreateResponse(Constants.Success, lstCustomers, null, null, condition);
                        }
                    }
                }
                else
                {
                    log.Info(string.Format("Authentication Failed for Get customers for view {0}, error {1}", viewName, role.Error));
                    return Response<List<VMCustomer>>.CreateResponse(Constants.Failed, null, role.Error);
                }
            }
            catch (Exception ex)
            {
                log.Error(string.Format("Error getting customers for view {0}\nError: {1}", viewName, ex));
                return Response<List<VMCustomer>>.CreateResponse(Constants.Failed, null, new ErrorHandler() { ErrorCode = Constants.CustomerErrorCode, Message = Constants.CustomerDetailsErrorMessage });
            }
        }

        [HttpGet]
        [Route("customers/fn_CheckDigit/{id}")]
        public Response<bool> fn_CheckDigit(long id)
        {
            bool flag = false;
            var client = CustomerDetails((int)id);

            //uncomment this on local environment
            //using (var result = new BackOfficeWebServiceClient("BackOfficeWebServer.NET"))
            //{
            //    result.UpdateClientCheckRuleStatus((int)id, "CDVCheck");
            //}
            try
            {
                if (client.Data != null)
                {
                    using (var result = new OrchestrationServiceClient("OrchestrationService.NET"))
                    {
                        try
                        {
                            flag = result.PerformCDV(client.Data.CategoryList[0].BankDetails.Bank.BankId, client.Data.CategoryList[0].BankDetails.AccountType.AccountTypeId, client.Data.CategoryList[0].BankDetails.AccountNo, client.Data.CategoryList[0].BankDetails.Bank.Code);
                        }
                        catch (Exception ex)
                        {
                            log.Error(string.Format("error performing CDV for client {0}\nError: {1}", id, ex));
                            throw;
                        }
                    }
                    if (flag)
                    {
                        try
                        {
                            using (var result = new BackOfficeWebServiceClient("BackOfficeWebServer.NET"))
                            {
                                result.UpdateClientCheckRuleStatus((int)id, "CDVCheck");
                            }
                        }
                        catch (Exception ex)
                        {
                            log.Error(string.Format("error updating client {0} rule status\nError: {1}", id, ex));
                            throw;
                        }
                        log.Info(string.Format("check digit success for customer {0}", id));
                        return Response<bool>.CreateResponse(Constants.Success, flag, null);
                    }
                    else
                    {
                        log.Info(string.Format("check digit failed for customer {0}. Invalid bank details.", id));
                        return Response<bool>.CreateResponse(Constants.Failed, flag, new ErrorHandler() { ErrorCode = Constants.CustomerErrorCode, Message = Constants.InvalidBankDetails });
                    }
                }
                else
                {
                    log.Info(string.Format("check digit failed for customer {0}. Client data null.", id));
                    return Response<bool>.CreateResponse(Constants.Failed, flag, new ErrorHandler() { ErrorCode = Constants.CustomerErrorCode, Message = Constants.CheckDigitExecutionErrorMessage });
                }
            }
            catch (Exception ex)
            {
                log.Error(string.Format("check digit failed for customer {0}\nError: {1}", id, ex));
                return Response<bool>.CreateResponse(Constants.Failed, flag, new ErrorHandler() { ErrorCode = Constants.CustomerErrorCode, Message = Constants.CheckDigitExecutionErrorMessage });
            }
        }

        [HttpGet]
        [Route("applications/fn_AVSCheck/{id}")]
        public Response<Integrations.AVSResponse> fn_AVSCheck(int id)
        {
            try
            {
                BackOfficeWebServer.AVSRCheck bankDetail = new BackOfficeWebServer.AVSRCheck();
                using (var result = new BackOfficeWebServiceClient("BackOfficeWebServer.NET"))
                {
                    try
                    {
                        ErrorHandler error = new ErrorHandler();
                        bankDetail = result.Getbankdetails(id, out error);
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
                        user_id = Convert.ToString(System.Configuration.ConfigurationManager.AppSettings["userid"]),
                        password = Convert.ToString(System.Configuration.ConfigurationManager.AppSettings["password"]),
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
                    var results = JsonConvert.DeserializeObject<Integrations.AVSResponse>(json);
                    if (results.report.ReportError.error_code != "00")
                    {
                        try
                        {
                            using (var result = new BackOfficeWebServiceClient("BackOfficeWebServer.NET"))
                            {
                                result.UpdateClientCheckRuleStatus(id, "AVSCheck");
                            }
                        }
                        catch (Exception ex)
                        {
                            log.Error(string.Format("error updating client {0} rule status\nError: {1}", id, ex));
                            throw;
                        }
                        log.Info(string.Format("AVS success for customer {0}", id));
                        return Response<Integrations.AVSResponse>.CreateResponse(Constants.Success, results, null);
                    }
                    else
                    {
                        log.Info(string.Format("AVS check failed for customer {0}. API failed. Error: {1}", id, results.report.ReportError.error_msg));
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
                    log.Info(string.Format("AVS check failed for customer {0}. Invalid bank details.", id));
                    return Response<Integrations.AVSResponse>.CreateResponse(Constants.Failed, null, new ErrorHandler() { ErrorCode = Constants.CustomerErrorCode, Message = Constants.InvalidBankDetails });
                }
            }
            catch (Exception ex)
            {
                log.Error(string.Format("check digit failed for customer {0}\nError: {1}", id, ex));
                return Response<Integrations.AVSResponse>.CreateResponse(Constants.Failed, null, new ErrorHandler() { ErrorCode = Constants.CustomerErrorCode, Message = Constants.AVSExecutionErrorMessage });
            }
        }

        private string getEditedFields(string editedFields, int clientId, int newStatus, BO_ObjectAPI type)
        {
            try
            {
                log.Info(string.Format("Get edited fields for client {0}", clientId));
                using (var result = new BackOfficeWebServiceClient("BackOfficeWebServer.NET"))
                {
                    var currentStatus = result.GetCustomerCategoryStatus(clientId).Where(o => o.CustomerObject == type).FirstOrDefault();
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
            catch(Exception ex)
            {
                log.Error(string.Format("error getting edited fields for client {0}\nError: {1}", clientId, ex));
                throw;
            }
        }
        
        private List<VMAccounts> GetCustomerAccounts(int clientId)
        {
            try
            {
                log.Info(string.Format("Get customer {0} accounts", clientId));
                List<VMAccounts> accounts = new List<VMAccounts>();
                using (var result = new BackOfficeWebServiceClient("BackOfficeWebServer.NET"))
                {
                    long[] accountIds = result.GetAccountIdListByClientId(clientId);
                    if (accountIds != null && accountIds.Count() > 0)
                    {
                        using (var res = new BackOfficeAccountServerClient("BackOfficeAccountServer.NET"))
                        {
                            accounts = res.AccountListById(accountIds).ToList();

                        }
                    }
                    return accounts;
                }
            }
            catch(Exception ex)
            {
                log.Error(string.Format("Error in getting customer {0} accounts\nError: {1}", clientId, ex));
                throw;
            }
        }

        [HttpGet]
        [Route("customer/getapplications/{clientId}")]
        public Response<List<VMClientApplication>> GetApplicationsByClientId(int clientId)
        {
            try
            {
                var role = this.HasAccess(BO_Object.Customers, BackOfficeEnum.Action.VIEW);
                if (role.Status == Constants.Success)
                {
                    List<VMClientApplication> clientApplications = new List<VMClientApplication>();
                    clientApplications = ClientApplications(clientId);
                    if (clientApplications != null)
                    {
                        log.Info(string.Format("Get customer {0} applications success. Application count: {1}", clientId, clientApplications.Count()));
                        return Response<List<VMClientApplication>>.CreateResponse(Constants.Success, clientApplications, new ErrorHandler());
                    }
                    else
                    {
                        log.Info(string.Format("Get customer {0} applications failed", clientId));
                        return Response<List<VMClientApplication>>.CreateResponse(Constants.Failed, null, new ErrorHandler() { ErrorCode = Constants.CustomerErrorCode, Message = Constants.CustomerDetailsErrorMessage });
                    }
                }
                else
                {
                    log.Info(string.Format("Authenticaion Error in getting customer {0} applications\nError: {1}", clientId, role.Error));
                    return Response<List<VMClientApplication>>.CreateResponse(Constants.InvalidToken, null, new ErrorHandler() { ErrorCode = Constants.AuthenticationFailedCode, Message = role.Error.ToString() });
                }

            }
            catch (Exception ex)
            {
                log.Error(string.Format("Error in getting customer {0} applications\nError: {1}", clientId, ex));
                return Response<List<VMClientApplication>>.CreateResponse(Constants.Failed, null, new ErrorHandler() { ErrorCode = Constants.CustomerErrorCode, Message = Constants.GetApplicationsErrorMessage });
            }
        }
        
        private List<VMClientApplication> ClientApplications(int clientId)
        {
            try
            {
                log.Info(string.Format("Get customer {0} applications", clientId));
                List<VMClientApplication> ClientApplications = new List<VMClientApplication>();
                using (var client = new BackOfficeWebServiceClient("BackOfficeWebServer.NET"))
                {
                    ClientApplications = client.GetApplicationsByClientId(clientId).ToList();
                    return ClientApplications;
                }
            }
            catch (Exception ex)
            {
                log.Error(string.Format("Error in getting customer {0} applications\nError: {1}", clientId, ex));
                throw;
            }

        }

        [HttpPost]
        [Route("customer/fn_BinCheck/{id}")]
        public Response<CardSwipe.BinCheck> fn_BinCheck(int id)
        {
            try
            {
                log.Info(string.Format("bin check for number {0}", id));
                var serviceResult = new Integrations.Application_Integrations().fn_BinCheck(Convert.ToString(id));
                if (serviceResult.ResponseCode == "00")
                {
                    log.Info(string.Format("bin check for number {0} success", id));
                    return Response<CardSwipe.BinCheck>.CreateResponse(Constants.Success, serviceResult, null);
                }
                else
                {
                    log.Info(string.Format("bin check for number {0} failed. API failed. Error: {1}", id, serviceResult.result));
                    return Response<CardSwipe.BinCheck>.CreateResponse(Constants.Failed, serviceResult, new ErrorHandler() { ErrorCode = serviceResult.ResponseCode, Message = serviceResult.result });
                }
            }
            catch (Exception ex)
            {
                log.Error(string.Format("Error in bin check. Number {0}\nError: {1}", id, ex));
                return Response<CardSwipe.BinCheck>.CreateResponse(Constants.Failed, null, new ErrorHandler() { ErrorCode = Constants.CustomerErrorCode, Message = Constants.BinCheckExecutionErrorMessage });
            }
        }

        [HttpGet]
        [Route("customer/search")]
        public Response<VMCustomerDetails> SearchCustomer(string query)
        {
            try
            {
                query = query.Trim();
                log.Info(string.Format("Customer search. Query: {0}", query));
                var role = this.HasAccess(BO_Object.Customers, BackOfficeEnum.Action.VIEW);
                if (role.Status == Constants.Success)
                {
                    var customer = SearchCustomerFromBOS(query);
                    ////if (customer.Status == Constants.Failed)
                    ////{
                    ////    var response = SearchCustomerFromASS(query);
                    ////    return response;
                    ////}
                    ////else
                    //if (customer.Status != Constants.Failed)
                    //{
                    if (customer.Status == Constants.Failed)
                    {
                        var response = SearchCustomerFromASS(query);
                        return response;
                    }
                    else { 
                        //get customer applications
                        var clientApplications = ClientApplications((int)customer.Data.ClientId);
                        customer.Data.ClientApplications = new List<VMClientApplication>();
                        if (clientApplications != null && clientApplications.Count() > 0)
                        {
                            using (UnitOfWork uow = new UnitOfWork())
                            {
                                using (var result = new BackOfficeWebServiceClient("BackOfficeWebServer.NET"))
                                {
                                    foreach (var application in clientApplications)
                                    {
                                        var subObjStatus = result.GetApplicationCategoryStatus(application.ApplicationId);
                                        var statusId = subObjStatus?.FirstOrDefault(s => s.applicationObject == BO_ObjectAPI.disbursement)?.NewStatusId;
                                        if(statusId != null && statusId < 15)
                                        {
                                            customer.Data.ClientApplications.Add(application);
                                        }
                                    }
                                }
                            }
                        }
                        customer.Data.ClientApplications = customer.Data.ClientApplications?.OrderByDescending(x => x.CreateDate).ToList();
                        var actions = Action((long)customer.Data.ClientId).Data.Actions;
                        customer.Data.Actions = actions.Where(x => x.ActionId == (Int64)BackOfficeEnum.Action.NEW_APPLICATION).ToList();
                    }
                    return customer;
                }
                log.Info(string.Format("Authenticaion Error in customer search. Query: {0}\nError: {1}", query, role.Error));
                return Response<VMCustomerDetails>.CreateResponse(Constants.Failed, null, role.Error);
            }
            catch (Exception ex)
            {
                log.Error(string.Format("Error in customer search. Query: {0}\nError: {1}", query, ex));
                return Response<VMCustomerDetails>.CreateResponse(Constants.Failed, null, new ErrorHandler() { ErrorCode = Constants.CustomerErrorCode, Message = Constants.CustomerDetailsErrorMessage });
            }
        }

        private Response<VMCustomerDetails> SearchCustomerFromBOS(string query, int Page = 0, int PageSize = 0)
        {
            try
            {
                log.Info(string.Format("Customer search from BO. Query: {0}", query));
                List<VMCustomer> customers = new List<VMCustomer>();
                using (var result = new BackOfficeWebServiceClient("BackOfficeWebServer.NET"))
                {
                    var clientId = result.CustomerSearchFromBOS(query);
                    if (clientId > 0)
                        return GetCustomerById(clientId);
                    return Response<VMCustomerDetails>.CreateResponse(Constants.Failed, null, new ErrorHandler() { ErrorCode = Constants.CustomerErrorCode, Message = Constants.CustomerDetailsNotFound });
                }
            }
            catch (Exception ex)
            {
                log.Error(string.Format("Error in customer search from BO. Query: {0}\nError: {1}", query, ex));
                throw;
            }
        }

        private Response<VMCustomerDetails> SearchCustomerFromASS(string query)
        {
            try
            {
                log.Info(string.Format("Customer search from ASS. Query: {0}", query));
                long clientId = 0;
                var connStr = System.Configuration.ConfigurationManager.ConnectionStrings["ASS"].ConnectionString;
                var assSchema = System.Configuration.ConfigurationManager.AppSettings["ASSSchema"];
                using (NpgsqlConnection connection = new NpgsqlConnection(connStr))
                {
                    connection.Open();
                    string sql = string.Format("select * from {1}.client where identno='{0}'", query, assSchema);
                    try
                    {
                        using (NpgsqlCommand command = new NpgsqlCommand(sql, connection))
                        {
                            using (NpgsqlDataReader reader = command.ExecuteReader())
                            {
                                while (reader.Read())
                                {
                                    clientId = CopyClientFromASS(reader);
                                }
                            }
                        }
                        if (clientId > 0)
                        {
                            var custDetails= GetCustomerById((int)clientId);
                            custDetails.Data.Actions = Action(clientId).Data.Actions.Where(x => x.ActionId == (Int64)BackOfficeEnum.Action.NEW_APPLICATION).ToList();
                            return custDetails;
                        }
                        else
                        {

                            log.Info(string.Format("Customer not found in ASS. Query: {0}", query));
                            VMCustomerDetails details = new VMCustomerDetails();
                            details.Actions = Action(0).Data.Actions.Where(x=>x.ActionId==(Int64)BackOfficeEnum.Action.NEW_APPLICATION).ToList();
                            return Response<VMCustomerDetails>.CreateResponse(Constants.Success, details, new ErrorHandler() { ErrorCode = Constants.CustomerErrorCode, Message = Constants.CustomerDetailsNotFound });
                        }
                    }
                    catch (NpgsqlException ex)
                    {
                        log.Error(string.Format("NpgsqlException in customer search from ASS. Query: {0}\nError: {1}", query, ex));
                        connection.Close();
                        throw;
                    }
                    finally
                    {
                        connection.Close();
                    }
                }
            }
            catch (Exception ex)
            {
                log.Error(string.Format("Error in customer search from ASS. Query: {0}\nError: {1}", query, ex));
                throw;
            }
        }

        private long CopyClientFromASS(NpgsqlDataReader reader)
        {
            try
            {
                log.Info("Copy personal details");
                var clientId = CopyProfile(reader);
                if (clientId > 0)
                {
                    try
                    {
                        log.Info("Copy bank details");
                        var bankId = CopyBankDetails(reader, clientId);
                    }
                    catch (Exception ex)
                    {
                        log.Error(string.Format("Error copying bank details from ASS\nError: {0}",ex));
                        throw;
                    }
                    try
                    {
                        log.Info("Copy employer details");
                        var empId = CopyEmployerDetails(reader, clientId);
                    }
                    catch (Exception ex)
                    {
                        log.Error(string.Format("Error copying employer details from ASS\nError: {0}", ex));
                        throw;
                    }
                    return clientId;
                }
                return 0;
            }
            catch (Exception ex)
            {
                log.Error(string.Format("Error copying persona; details from ASS.\nError: {0}", ex));
                throw;
            }
        }

        private string CopyEmployerDetails(NpgsqlDataReader reader, long clientId)
        {
            try
            {
                log.Info(string.Format("Copy employer details for client {0}", clientId));
                EmployerDTO emp = new EmployerDTO();
                emp.Address = new AddressDto();
                emp.Address.AddressLine1 = reader["waddr1"] == DBNull.Value ? null : Convert.ToString(reader["waddr1"])?.Trim();
                emp.Address.AddressLine2 = reader["waddr2"] == DBNull.Value ? null : Convert.ToString(reader["waddr2"])?.Trim();
                emp.Address.AddressLine3 = reader["waddr3"] == DBNull.Value ? null : Convert.ToString(reader["waddr3"])?.Trim();
                emp.Address.AddressLine4 = reader["waddr4"] == DBNull.Value ? null : Convert.ToString(reader["waddr4"])?.Trim();
                emp.Address.PostalCode = reader["wpostcode"] == DBNull.Value ? null : Convert.ToString(reader["wpostcode"])?.Trim();
                emp.CellNo = reader["worktel"] == DBNull.Value ? null: Convert.ToString(reader["worktel"])?.Trim();
                emp.EmployeeNo = reader["emp_no"] == DBNull.Value ? null : Convert.ToString(reader["emp_no"])?.Trim();
                emp.EmployerCode = reader["employcode"] == DBNull.Value ? 0 : Convert.ToInt32(reader["employcode"]);
                emp.OccupationCode = reader["occup"] == DBNull.Value ? null : Convert.ToString(reader["occup"])?.Trim();
                emp.NetPay = reader["netpay"] == DBNull.Value ? 0 : reader["netpay"] == DBNull.Value ? 0 : Convert.ToDecimal(reader["netpay"]);
                emp.TelephoneNo = reader["worktel"] == DBNull.Value ? null : Convert.ToString(reader["worktel"])?.Trim();
                emp.StartDate = Convert.ToDateTime(reader["emp_date"] == DBNull.Value ? null : reader["emp_date"]);
                emp.SupervisorName = reader["supervisor"] == DBNull.Value ? null : Convert.ToString(reader["supervisor"])?.Trim(); 
                emp.EmployerId = 0;
                emp.Name = reader["workname"] == DBNull.Value ? null : Convert.ToString(reader["workname"])?.Trim();
                //emp.NetPay =
                //emp.OccupationCode = dont match
                emp.SalaryType = GetSalaryType(reader["salaryfreq"] == DBNull.Value ? null : Convert.ToString(reader["salaryfreq"])?.Trim());
                emp.PayDay = GetPayDay(emp.SalaryType, reader["payday"]);
                //emp.SupervisorName = Convert.ToString(reader[""]);
                //emp.TelephoneNo = 
                emp.Comment = "Employer details copied from ASS.";

                using (var result = new BackOfficeWebServiceClient("BackOfficeWebServer.NET"))
                {
                    var provinces = result.GetProvince();
                    emp.Address.Province = provinces.Where(p => p.PostalCode == Convert.ToString(reader["wpostcode"] == DBNull.Value ? null : reader["wpostcode"])?.Trim()).FirstOrDefault();
                }

                var response = AddUpdateEmployer((int)clientId, emp, 1);
                if (response.Status == Constants.Success)
                    return response.Data;
                return "";
            }
            catch (Exception ex)
            {
                log.Error(string.Format("Error copying employer details for client {0}\nError: {1}",clientId, ex));
                throw;
            }
        }

        private int GetSalaryType(string type)
        {
            try
            {
                log.Info(string.Format("Get salary type for input {0}", type));
                using (var result = new BackOfficeWebServiceClient("BackOfficeWebServer.NET"))
                {
                    var salTypes = result.GetSalaryType();
                    var salaryType = string.Empty;
                    if (type == "M")
                        salaryType = "monthly";
                    else if (type == "W")
                        salaryType = "weekly";
                    else if (type == "B")
                        salaryType = "fortnightly";
                    else
                        return 0;
                    return salTypes.FirstOrDefault(s => s.Description.ToLower() == salaryType).AddressTypeId;
                }
            }
            catch (Exception ex)
            {
                log.Error(string.Format("Error getting salary type for input {0}\nError: {1}", type, ex));
                throw;
            }
        }

        private int GetPayDay(int salaryType, object value)
        {
            try
            {
                if(value == DBNull.Value)
                {
                    return 0;
                }

                if (salaryType == (int)SalaryFrequency.Weekly)
                {
                    switch (Convert.ToString(value).ToUpper().Trim())
                    {
                        case "MON":
                            return 1;
                        case "TUE":
                            return 2;
                        case "WED":
                            return 3;
                        case "THU":
                            return 4;
                        case "FRI":
                            return 5;
                        case "SAT":
                            return 6;
                        case "SUN":
                            return 7;
                        default:
                            return 0;
                    }
                }
                else
                {
                    int days = 0;
                    int.TryParse(Convert.ToString(value).Trim(), out days);
                    if (days == 0)
                    {
                        return 0;
                    }
                    return Convert.ToInt32(value);
                }
            }
            catch (Exception ex)
            {
                log.Error(string.Format("Error in geting PayDay for input {0}\nError: {1}", value, ex));
                throw;
            }
        }


        private string CopyBankDetails(NpgsqlDataReader reader, long clientId)
        {
            try
            {
                log.Info(string.Format("Copy bank details for client {0}", clientId));
                BankDetailDto bankdetails = new BankDetailDto();
                BankDto bank = new BankDto();
                bankdetails.BankDetailId = 0;
                //TODO: change account name
                bankdetails.AccountName = Convert.ToString(reader["firstname"] == DBNull.Value ? null : reader["firstname"])?.Trim() + " " + Convert.ToString(reader["surname"] == DBNull.Value ? null : reader["surname"])?.Trim();
                bankdetails.AccountNo = reader["bankacc"] == DBNull.Value ? null : Convert.ToString(reader["bankacc"])?.Trim();
                bankdetails.CardNumber = reader["cardno"] == DBNull.Value ? null : Convert.ToString(reader["cardno"])?.Trim();
                //bankdetails.IsActive = 
                //bankdetails.IsVerified = 
                bankdetails.AccountType = GetBankAccountType(reader["bnktype"] == DBNull.Value ? null : Convert.ToString(reader["bnktype"])?.Trim());
                bank.BankNo = reader["bank"] == DBNull.Value ? null : Convert.ToString(reader["bank"])?.Trim();
                bankdetails.Bank = bank;
                //bankdetails.Period = 
                bankdetails.Comment = "Bank details copied from ASS.";
                var response = AddClientBankDetails(bankdetails, (int)clientId, 1);
                if (response.Status == Constants.Success)
                    return response.Data;
                return null;
            }
            catch (Exception ex)
            {
                log.Error(string.Format("Error copying bank details for client {0}\nError: {1}", clientId, ex));
                throw;
            }
        }

        private BankAccountTypeDto GetBankAccountType(string type)
        {
            try
            {
                log.Info(string.Format("Get bank account type for input {0}", type));
                using (var result = new BackOfficeWebServiceClient("BackOfficeWebServer.NET"))
                {
                    var bankAccountTypes = result.GetBankAccountTypes();
                    return bankAccountTypes.FirstOrDefault(b => b.AccountTypeId.ToString() == type);
                }
            }
            catch (Exception ex)
            {
                log.Error(string.Format("Error getting bank account type for input {0}\nError: {1}", type, ex));
                throw;
            }
        }

        private long CopyProfile(NpgsqlDataReader reader)
        {
            try
            {
                using (UnitOfWork uow = new UnitOfWork())
                {
                    log.Info(string.Format("Copy profile details for client"));

                    VMClient vmclient = new VMClient();
                    vmclient.Client = new ClientDto();
                    string legacyBranchNum = reader["brnum"] == DBNull.Value ? null : Convert.ToString(reader["brnum"])?.Trim();
                    var branch = new XPQuery<BRN_Branch>(uow).Where(b => b.LegacyBranchNum == legacyBranchNum).FirstOrDefault();

                    vmclient.Client.Branch = new BRN_Branchdto();
                    if (branch != null)
                    {
                        vmclient.Client.Branch.BranchId = branch.BranchId;
                        vmclient.Client.Branch.BranchName = branch.BranchName?.Trim();

                    }
                    vmclient.Client.CellNo = reader["cell"] == DBNull.Value ? null : Convert.ToString(reader["cell"])?.Trim().TrimLeadingZero();
                    vmclient.Client.DateOfBirth =  Convert.ToDateTime(reader["birthdate"] == DBNull.Value ? null : reader["birthdate"]);
                    vmclient.Client.Email = reader["email_addr"] == DBNull.Value ? null : Convert.ToString(reader["email_addr"]).Trim();
                    vmclient.Client.Firstname = reader["firstname"] == DBNull.Value ? null : Convert.ToString(reader["firstname"])?.Trim();
                    vmclient.Client.Gender = Convert.ToChar(reader["gender"] == DBNull.Value ? null : reader["gender"]);
                    vmclient.Client.IDNumber = reader["identno"] == DBNull.Value ? null : Convert.ToString(reader["identno"])?.Trim();
                    vmclient.Client.LanguageId = GetLanguage(reader["pref_lang"] == DBNull.Value ? null : Convert.ToString(reader["pref_lang"])?.Trim());
                    vmclient.Client.MaritalStatus = GetMaritalStatus(reader["clstat"] == DBNull.Value ? null : Convert.ToString(reader["clstat"])?.Trim());
                    vmclient.Client.Surname = reader["surname"] == DBNull.Value ? null : Convert.ToString(reader["surname"])?.Trim();
                    vmclient.Client.TelephoneNumber = reader["hometel"] == DBNull.Value ? null : Convert.ToString(reader["hometel"])?.Trim();
                    vmclient.Client.Title = reader["title"] == DBNull.Value ? null : Convert.ToString(reader["title"])?.Trim();

                    vmclient.Address = new AddressDto();
                    vmclient.Address.AddressLine1 =  reader["haddr1"] == DBNull.Value ? null : Convert.ToString(reader["haddr1"])?.Trim();
                    vmclient.Address.AddressLine2 =  reader["haddr2"] == DBNull.Value ? null : Convert.ToString(reader["haddr2"])?.Trim();
                    vmclient.Address.AddressLine3 =  reader["haddr3"] == DBNull.Value ? null : Convert.ToString(reader["haddr3"])?.Trim();
                    vmclient.Address.PostalCode = reader["hpostcode"] == DBNull.Value ? null : Convert.ToString(reader["hpostcode"])?.Trim();
                    vmclient.Comment = "Client copied from ASS.";
                    using (var result = new BackOfficeWebServiceClient("BackOfficeWebServer.NET"))
                    {
                        var countries = result.GetCountry();
                        vmclient.Client.CountryOfBirthId = countries.Where(c => c.Code == Convert.ToString(reader["country"] == DBNull.Value ? null : reader["country"])?.Trim()).FirstOrDefault().CountryId;

                        var races = result.GetRaces();
                        vmclient.Client.Ethnicity = new EthnicityDTO();
                        vmclient.Client.Ethnicity.EthnicityId = races.Where(e => e.Code == Convert.ToString(reader["race"] == DBNull.Value ? null : reader["race"])?.Trim()).FirstOrDefault().EthnicityId;

						var PropertyOwnership = result.GetPropertyOwnership();
						vmclient.Client.PropertyOwnership = new PropertyOwnershipDTO();
						//vmclient.Client.PropertyOwnership.PropertyOwnershipId = PropertyOwnership.Where(e => e.Code == Convert.ToString(reader["PropertyOwnership"] == DBNull.Value ? null : reader["PropertyOwnership"])?.Trim()).FirstOrDefault().PropertyOwnershipId;

						var provinces = result.GetProvince();
                        vmclient.Address.Province = provinces.Where(p => p.PostalCode == Convert.ToString(reader["hpostcode"] == DBNull.Value ? null : reader["hpostcode"])?.Trim()).FirstOrDefault();
                    }

                    var response = CreateClient(vmclient);
                    if (response.Status == Constants.Success)
                        return response.Data;
                    return 0;
                }
            }
            catch (Exception ex)
            {
                log.Error(string.Format("Error copying personal details\nError: {0}", ex));
                throw;
            }
        }

        private MaritalStatusDTO GetMaritalStatus(string maritalStatus)
        {
            try
            {
                log.Info(string.Format("Get marital status for input {0}", maritalStatus));
                using (var result = new BackOfficeWebServiceClient("BackOfficeWebServer.NET"))
                {
                    if (!string.IsNullOrEmpty(maritalStatus))
                    {
                        maritalStatus = maritalStatus.Trim();
                        var maritalstates = result.GetMaritalStatus();
                        var state = string.Empty;
                        if (maritalStatus == "S")
                            state = "single";
                        else if (maritalStatus == "M")
                            state = "married";
                        else if (maritalStatus == "D")
                            state = "divorced";
                        else if (maritalStatus == "W")
                            state = "widow";
                        else
                            return new MaritalStatusDTO();
                        return maritalstates.FirstOrDefault(l => l.Description.ToLower() == state);
                    }
                    return new MaritalStatusDTO();
                }
            }
            catch (Exception ex)
            {
                log.Error(string.Format("Error getting marital status for input {0}\nError: {1}", maritalStatus, ex));
                throw;
            }
        }

        private int GetLanguage(string language)
        {
            try
            {
                log.Info(string.Format("Get language for input {0}", language));
                using (var result = new BackOfficeWebServiceClient("BackOfficeWebServer.NET"))
                {
                    language = language.Trim();
                    var languages = result.GetLanguages();
                    var code = string.Empty;
                    if (language == "A")
                        code = "AFR";
                    else if (language == "E")
                        code = "ENG";
                    else if (language == "Z")
                        code = "ZUL";
                    else if (language == "X")
                        code = "XHO";
                    else if (language == "T")
                        code = "TSH";
                    else code = "OTH";
                    return languages.FirstOrDefault(l => l.LanguageCode == code).LanguageId;
                }
            }
            catch (Exception ex)
            {
                log.Error(string.Format("Error getting language for input {0}\nError: {1}", language, ex));
                throw;
            }

        }

        public Response<ApplicationDto> CreateApplication(int id, VMCustomerDetails dto)
        {
            ApplicationController ctrl = new ApplicationController();
            return ctrl.CreateApplication(id, dto);
        }

        [Route("customers/{id}/gethistory/{subobject}/{version?}")]
        public Response<VMCustomerHistory> GetCustomerHistory(int id, string subobject, int version = 0)
        {
            try
            {
                var role = this.HasAccess(BO_Object.Applications, BackOfficeEnum.Action.VIEW);
                if (role.Status == Constants.Success)
                {
                    log.Info(string.Format("GetCustomerHistory subobject {0}, version {1}", subobject, version));
                    VMCustomerHistory custHistory = new VMCustomerHistory();
                    using (UnitOfWork uow = new UnitOfWork())
                    {
                        custHistory.LatestVersion = new XPQuery<BOS_CustomerHistory>(uow).Where(h => h.ClientId == id && h.Category.ToLower() == subobject.ToLower()).Max(x => x.Version);
                        if (version <= 0)
                        {
                            version = custHistory.LatestVersion;
                        }
                        var history = new XPQuery<BOS_CustomerHistory>(uow).FirstOrDefault(h => h.ClientId == id && h.Category.ToLower() == subobject.ToLower() && h.Version == version);
                        if (history != null)
                        {
                            var historyDto = Mapper.Map<BOS_CustomerHistory, BOS_CustomerHistoryDTO>(history);
                            custHistory.History = historyDto;
                            custHistory.CurrentVersion = version;
                            custHistory.OldVersion = version - 1;
                            return Response<VMCustomerHistory>.CreateResponse(Constants.Success, custHistory, null);
                        }
                    }
                    return Response<VMCustomerHistory>.CreateResponse(Constants.Failed, null, new ErrorHandler { ErrorCode = Constants.CustomerErrorCode, Message = Constants.DetailsNotFoundErrorMessage });
                }
                else
                {
                    log.Info(role.Error);
                    return Response<VMCustomerHistory>.CreateResponse(Constants.Failed, null, role.Error);
                }
            }
            catch (Exception ex)
            {
                log.Error(ex);
                return Response<VMCustomerHistory>.CreateResponse(Constants.Failed, null, new ErrorHandler { ErrorCode = Constants.CustomerErrorCode, Message = Constants.DetailsNotFoundErrorMessage });
            }
        }
        
        
    }
}
