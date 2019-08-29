using Atlas.Common.Common;
using FrameworkLibrary.ResponseBase;
using FrameworkLibrary.ExceptionBase;
using Atlas.Domain.DTO;
using Atlas.Domain.Model;
using BackOfficeServer.BackOfficeWebServer;
using BackOfficeServer.ViewModels;
using DevExpress.Xpo;
using Newtonsoft.Json;
using System;
using System.Linq;
using System.Reflection;
using System.Web;
using System.Web.Http;
using Atlas.Common.Utils;
using Atlas.Online.Data.Models.DTO;
using Atlas.Online.Data.Models.Dto;
using static Atlas.Common.Utils.BackOfficeEnum;
using BackOfficeServer.Common;
using System.Data;
using log4net;
using FrameworkLibrary.Common;
using Atlas.Domain.DTO.Account;
using Atlas.Domain.Model.BOS;
using static BackOfficeServer.Common.ApplicationCaseUtil;

namespace BackOfficeServer.Controllers
{
    public class CommonController : BaseController
    {
        private static readonly ILog log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        /// <summary>
        /// This method performs said actions on the given object
        /// </summary>
        /// <param name="objName">object on which action is to be performed</param>
        /// <param name="id">id of the object</param>
        /// <param name="actionName">action to be performed</param>
        /// <param name="data">paramaters, if any</param>
        /// <returns>API Response</returns>

        [HttpPost]
        [Route("Actions/{objName}/{id}/{Category?}/{documentId?}")]
        public Response<object> BackOfficeActions(string objName, long id, string action, [FromBody]object data, string category = "", string documentId = "")
        {
            try
            {
                log.Info(string.Format("Execute action: object {0}, id {1}, action {2}, category {3}", objName, id, action, category));
                category = category.ToLower();
                objName = objName.ToLower();
                action = action.ToLower();

                var obj = Enum.GetValues(typeof(BO_Object)).Cast<BO_Object>().Where(o => o.ToString().ToLower() == objName).FirstOrDefault();
                var act = Enum.GetValues(typeof(BackOfficeEnum.Action)).Cast<BackOfficeEnum.Action>().Where(a => a.ToString().ToLower() == action).FirstOrDefault();
                var role = HasAccess(obj, act);

                if (role.Status == Constants.Success)
                {
                    log.Info("role authorization success");
                    BOS_WorkFlowService svcWorkflow = new BOS_WorkFlowService();
                    using (var uow = new UnitOfWork())
                    {
                        string api = "{id}/" + category;
                        int statusId = 0;

                        try
                        {
                            log.Info("get current status of category");
                            if (objName == BO_Object.Customers.ToString().ToLower())
                                statusId = ObjectStateUtil.GetCustomerCategoryStatus((int)id, category);

                            else if (objName == BO_Object.Applications.ToString().ToLower())
                                statusId = ObjectStateUtil.GetApplicationCategoryStatus((int)id, category);

                            else if (objName == BO_Object.Accounts.ToString().ToLower())
                                statusId = ObjectStateUtil.GetAccountsCategoryStatus(id, category);

                            else if (objName == BO_Object.Repayment.ToString().ToLower())
                                statusId = ObjectStateUtil.GetRepaymentStatus(id, category);
                            else if (objName == BO_Object.Users.ToString().ToLower())
                                statusId = 0;

                            else
                            {
                                log.Info(string.Format("Invalid action: object {0}, id {1}, action {2}, category {3}", objName, id, action, category));
                                return Response<object>.CreateResponse(Constants.Failed, null,
                                                        new ErrorHandler()
                                                        {
                                                            ErrorCode = Constants.ActionsErrorCode,
                                                            Message = Constants.ActionsInvalidMessage
                                                        });
                            }

                        }
                        catch (Exception ex)
                        {
                            log.Error(string.Format("Error getting status of the category: object {0}, id {1}, action {2}, category {3}\nError: {4}", objName, id, action, category, ex));
                            throw;
                        }

                        try
                        {
                            log.Info("get workflow service");
                            if (statusId != 0)
                                svcWorkflow = new XPQuery<BOS_WorkFlowService>(uow).Where(x => x.Object.ObjectName.ToLower() == objName && x.Action.ActionName.ToLower() == action && x.API.ToLower() == api && x.Status.StatusId == statusId).FirstOrDefault();
                            else
                                svcWorkflow = new XPQuery<BOS_WorkFlowService>(uow).Where(x => x.Object.ObjectName.ToLower() == objName && x.Action.ActionName.ToLower() == action && x.API.ToLower() == api && x.Status == null).FirstOrDefault();
                        }
                        catch (Exception ex)
                        {
                            log.Error(string.Format("Error getting workflow service record: object {0}, id {1}, action {2}, category {3}\nError: {4}", objName, id, action, category, ex));
                            throw;
                        }

                        if (svcWorkflow != null)
                        {
                            log.Info("check if rules met");
                            if (WorkflowUtil.AreRulesMet(id, svcWorkflow.WorkFlowServiceId, (int)obj, out string ruleName))
                            {
                                switch (obj.ToString())
                                {
                                    case "Customers":
                                        return InvokeCustomerActions(svcWorkflow, (int)id, category, action, Convert.ToString(data), api, documentId);
                                    case "Accounts":
                                        return InvokeAccountActions(svcWorkflow, id, category, action, Convert.ToString(data), api, documentId);
                                    case "Applications":
                                        return InvokeApplicationActions(svcWorkflow, (int)id, category, action, Convert.ToString(data), api, documentId);
                                    case "Repayment":
                                        return InvokeRepaymentActions(svcWorkflow, (int)id, category, action, Convert.ToString(data), api, documentId);

                                    default:
                                        {
                                            log.Info(string.Format("Invalid object: object {0}, id {1}, action {2}, category {3}", objName, id, action, category));
                                            return Response<object>.CreateResponse(Constants.Failed, null,
                                                new ErrorHandler()
                                                {
                                                    ErrorCode = Constants.ActionsErrorCode,
                                                    Message = Constants.ActionsInvalidMessage
                                                });
                                        }
                                }
                            }
                            log.Info(string.Format("Pre conditions - {0} is pending: object {1}, id {2}, action {3}, category {4}", ruleName, objName, id, action, category));
                            return Response<object>.CreateResponse(Constants.Failed, null, new ErrorHandler() { ErrorCode = Constants.ActionsErrorCode, Message = string.Format(Constants.PreConditionsNotMet, ruleName) });
                        }
                        log.Info(string.Format("workflow service not found: object {0}, id {1}, action {2}, category {3}", objName, id, action, category));
                        return Response<object>.CreateResponse(Constants.Failed, null, new ErrorHandler() { ErrorCode = Constants.ActionsErrorCode, Message = Constants.ActionsInvalidMessage });
                    }
                }
                else
                {
                    log.Info(string.Format("Authorization failed: object {0}, id {1}, action {2}, category {3}", objName, id, action, category));
                    return Response<object>.CreateResponse(Constants.Failed, null, new ErrorHandler() { ErrorCode = Constants.AuthorizationFailedCode, Message = Constants.AuthorizationFailed });
                }
            }
            catch (Exception ex)
            {
                log.Error(string.Format("Error executing action: object {0}, id {1}, action {2}, category {3}\nError: {4}", objName, id, action, category, ex));
                return Response<object>.CreateResponse(Constants.Failed, null, new ErrorHandler() { ErrorCode = Constants.AccountErrorCode, Message = Constants.ActionsExecutionFoundMessage });
            }
        }

        /// <summary>
        /// This method invokes the said action to be performed on the customer object
        /// </summary>
        /// <param name="svcWorkflow">workflow service object that determines the next state of the object when action is performed</param>
        /// <param name="id">id of the object</param>
        /// <param name="action">action to be performed</param>
        /// <param name="data">paramaters, if any</param>
        private Response<object> InvokeCustomerActions(BOS_WorkFlowService svcWorkflow, int id, string category, string action, string data, string api, string documentId)
        {
            try
            {
                log.Info(string.Format("Invoke customer action: id {0}, action {1}, category {2}", id, action, category));

                var type = Type.GetType(typeof(CustomersController).ToString());
                string editedFields = string.Empty;
                MethodInfo methodInfo = null;
                methodInfo = typeof(CustomersController).GetMethod(svcWorkflow.TransitionFunction);
                if (methodInfo != null)
                {
                    log.Info("get parameter for method");
                    ParameterInfo[] parameters = methodInfo.GetParameters();
                    if (parameters == null)
                    {
                        log.Info(string.Format("no parameters found. invoke method"));
                        methodInfo.Invoke(type, null);
                    }
                    else
                    {
                        log.Info("parameters found");
                        object classInstance = Activator.CreateInstance(type, null);
                        CustomersController ctrlCustomer = new CustomersController();

                        if (category == BO_ObjectAPI.profile.ToString().ToLower())
                        {
                            log.Info("category: profile");
                            if (svcWorkflow.TransitionFunction.ToLower() == "editclient")
                            {
                                try
                                {
                                    log.Info("Edit client {}");
                                    VMClient customer = new VMClient();
                                    customer = JsonConvert.DeserializeObject<VMClient>(data);
                                    var result = ctrlCustomer.EditClient(id, customer);

                                    if (result.Status.ToLower() != "success")
                                        return Response<object>.CreateResponse(Constants.Failed, null, result.Error);

                                    editedFields = result.Data;
                                    ctrlCustomer.RejectAuthorization(id, Convert.ToInt32(svcWorkflow.NewStatus.StatusId));
                                }
                                catch (Exception ex)
                                {
                                    log.Error(string.Format("Error editng client: id {0}, action {1}, category {2}\nError: {3}", id, action, category, ex));
                                    throw;
                                }
                            }
                            ctrlCustomer.UpdateCustomerStatus(id, Convert.ToInt32(svcWorkflow.NewStatus.StatusId), action, data, editedFields);
                        }
                        else if (category == BO_ObjectAPI.bankdetails.ToString().ToLower())
                        {
                            log.Info("category: bank details");
                            if (svcWorkflow.TransitionFunction.ToLower() == "addclientbankdetails")
                            {
                                try
                                {
                                    log.Info("Edit bank details");
                                    BankDetailDto bankdetailsDTO = new BankDetailDto();
                                    bankdetailsDTO = JsonConvert.DeserializeObject<BankDetailDto>(data);
                                    int clientid = Convert.ToInt32(id);
                                    var editDetails = ctrlCustomer.AddClientBankDetails(bankdetailsDTO, clientid, Convert.ToInt32(svcWorkflow.NewStatus.StatusId));
                                    if (editDetails.Status == Constants.Success)
                                    {
                                        editedFields = editDetails.Data;
                                        ctrlCustomer.RejectAuthorization(clientid, Convert.ToInt32(svcWorkflow.NewStatus.StatusId));
                                    }
                                    else
                                        return Response<object>.CreateResponse(editDetails.Status, editDetails.Data, editDetails.Error);
                                }
                                catch (Exception ex)
                                {
                                    log.Error(string.Format("Error editng bank details: id {0}, action {1}, category {2}\nError: {3}", id, action, category, ex));
                                    throw;
                                }
                            }
                            else if (svcWorkflow.TransitionFunction.ToLower() == "fn_checkdigit")
                            {
                                try
                                {
                                    log.Info("Execute fn_checkdigit action");
                                    var result = ctrlCustomer.fn_CheckDigit(id);
                                    if (result.Status.ToLower() != "success")
                                        return Response<object>.CreateResponse(Constants.Failed, null, new ErrorHandler() { ErrorCode = result.Error.ErrorCode, Message = result.Error.Message });
                                }
                                catch (Exception ex)
                                {
                                    log.Error(string.Format("Error executing fn_checkdigit action: id {0}, action {1}, category {2}\nError: {3}", id, action, category, ex));
                                    throw;
                                }
                            }
                            else if (svcWorkflow.TransitionFunction.ToLower() == "fn_avscheck")
                            {
                                try
                                {
                                    log.Info("Execute fn_avscheck action");
                                    var result = ctrlCustomer.fn_AVSCheck(id);
                                    if (result.Status.ToLower() != "success")
                                        return Response<object>.CreateResponse(Constants.Failed, null, new ErrorHandler() { ErrorCode = result.Error.ErrorCode, Message = result.Error.Message });
                                }
                                catch (Exception ex)
                                {
                                    log.Error(string.Format("Error executing fn_avscheck action: id {0}, action {1}, category {2}\nError: {3}", id, action, category, ex));
                                    throw;
                                }
                            }
                            else if (svcWorkflow.TransitionFunction.ToLower() == "fn_bincheck")
                            {
                                try
                                {
                                    log.Info("Execute fn_bincheck action");
                                    var result = ctrlCustomer.fn_BinCheck(!string.IsNullOrEmpty(documentId) ? Convert.ToInt32(documentId) : 0);
                                    if (result.Data.ResponseCode != "00")
                                        return Response<object>.CreateResponse(Constants.Failed, result.Data, new ErrorHandler() { ErrorCode = result.Data.ResponseCode, Message = result.Data.result });
                                }
                                catch (Exception ex)
                                {
                                    log.Error(string.Format("Error executing fn_bincheck action: id {0}, action {1}, category {2}\nError: {3}", id, action, category, ex));
                                    throw;
                                }
                            }
                            ctrlCustomer.UpdateClientBankDetails(id, Convert.ToInt32(svcWorkflow.NewStatus.StatusId), action, data, editedFields);
                        }

                        else if (category == BO_ObjectAPI.employerdetails.ToString().ToLower())
                        {
                            log.Info("category: employer");
                            if (svcWorkflow.TransitionFunction.ToLower() == "addupdateemployer")
                            {
                                try
                                {
                                    log.Info("add/update employer");
                                    EmployerDTO emp = new EmployerDTO();
                                    emp = JsonConvert.DeserializeObject<EmployerDTO>(data);
                                    Response<string> editDetails = ctrlCustomer.AddUpdateEmployer(id, emp, Convert.ToInt32(svcWorkflow.NewStatus.StatusId));
                                    if (editDetails.Status == Constants.Success)
                                    {
                                        editedFields = editDetails.Data;
                                        ctrlCustomer.RejectAuthorization(id, Convert.ToInt32(svcWorkflow.NewStatus.StatusId));
                                    }
                                    else
                                    {
                                        return Response<object>.CreateResponse(Constants.Failed, null, new ErrorHandler() { ErrorCode = editDetails.Error.ErrorCode, Message = editDetails.Error.Message });
                                    }

                                }
                                catch (Exception ex)
                                {
                                    log.Error(string.Format("Error add/update employer details: id {0}, action {1}, category {2}\nError: {3}", id, action, category, ex));
                                    throw;
                                }
                            }
                            ctrlCustomer.UpdateClientEmployerDetails(id, Convert.ToInt32(svcWorkflow.NewStatus.StatusId), action, data, editedFields);
                        }
                        else if (category == BO_ObjectAPI.master.ToString().ToLower())
                        {
                            log.Info("category: master");
                            if (svcWorkflow.TransitionFunction.ToLower() == "authorise")
                            {
                                try
                                {
                                    log.Info("Authorize customer");
                                    bool IsAuthorized = ctrlCustomer.Authorise(id, Convert.ToInt32(svcWorkflow.NewStatus.StatusId));
                                    if (IsAuthorized)
                                        ctrlCustomer.UpdateCustomerEveHistForAuthorization(id, data, svcWorkflow.NewStatus.Description.ToUpper(), action);
                                }
                                catch (Exception ex)
                                {
                                    log.Error(string.Format("Error authorizing customer: id {0}, action {1}, category {2}\nError: {3}", id, action, category, ex));
                                    throw;
                                }
                            }
                            else if (svcWorkflow.TransitionFunction.ToLower() == "rejectauthorization")
                            {
                                try
                                {
                                    log.Info("Reject customer");
                                    bool IsRejected = ctrlCustomer.RejectAuthorization(id, Convert.ToInt32(svcWorkflow.NewStatus.StatusId));
                                    if (IsRejected)
                                        ctrlCustomer.UpdateCustomerEveHistForAuthorization(id, data, svcWorkflow.NewStatus.Description.ToUpper(), action);
                                }
                                catch (Exception ex)
                                {
                                    log.Error(string.Format("Error rejecting customer: id {0}, action {1}, category {2}\nError: {3}", id, action, category, ex));
                                    throw;
                                }
                            }
                            else if (svcWorkflow.TransitionFunction.ToLower() == "verifycustomer")
                            {
                                try
                                {
                                    log.Info("verify customer");
                                    bool IsVerified = ctrlCustomer.VerifyCustomer(id, Convert.ToInt32(svcWorkflow.NewStatus.StatusId));
                                    if (IsVerified)
                                        ctrlCustomer.UpdateCustomerEveHistForAuthorization(id, data, svcWorkflow.NewStatus.Description.ToUpper(), action);
                                }
                                catch (Exception ex)
                                {
                                    log.Error(string.Format("Error verifying customer: id {0}, action {1}, category {2}\nError: {3}", id, action, category, ex));
                                    throw;
                                }
                            }
                            else if (svcWorkflow.TransitionFunction.ToLower() == "createapplication")
                            {
                                VMCustomerDetails dto = new VMCustomerDetails();
                                dto = JsonConvert.DeserializeObject<VMCustomerDetails>(data);
                                var applicationCreated = ctrlCustomer.CreateApplication(id, dto);
                                if (applicationCreated.Status == Constants.Success)
                                    return Response<object>.CreateResponse(Constants.Success, applicationCreated.Data, null);
                                else
                                    return Response<object>.CreateResponse(Constants.Failed, null, new ErrorHandler() { ErrorCode = applicationCreated.Error.ErrorCode, Message = applicationCreated.Error.Message });
                            }
                            else
                                ctrlCustomer.UpdateCustomerStatus(id, Convert.ToInt32(svcWorkflow.NewStatus.StatusId), action, data, editedFields);
                        }
                        var cust = ctrlCustomer.GetCustomerById(id);
                        log.Info(string.Format("Invoke customer action success: id {0}, action {1}, category {2}", id, action, category));
                        return Response<object>.CreateResponse(Constants.Success, cust.Data, null);
                    }
                }
                log.Info(string.Format("Transition function {0} not found. id {1}, action {2}, category {3}", svcWorkflow.TransitionFunction, id, action, category));
                return Response<object>.CreateResponse(Constants.Failed, null, new ErrorHandler() { ErrorCode = Constants.ActionsErrorCode, Message = Constants.ActionsInvalidMessage });
            }
            catch (Exception ex)
            {
                log.Error(string.Format("Error invoking customer action: id {0}, action {1}, category {2}\nError: {3}", id, action, category, ex));
                throw;
            }
        }

        /// <summary>
        /// This method invokes the said action to be performed on the account object
        /// </summary>
        /// <param name="svcWorkflow">workflow service object that determines the next state of the object when action is performed</param>
        /// <param name="id">id of the object</param>
        /// <param name="action">action to be performed</param>
        /// <param name="data">paramaters, if any</param>
        private Response<object> InvokeAccountActions(BOS_WorkFlowService svcWorkflow, long id, string category, string action, string data, string api, string documentId)
        {
            try
            {
                log.Info(string.Format("Invoke account action: id {0}, action {1}, category {2}", id, action, category));
                var type = Type.GetType(typeof(AccountsController).ToString());
                string editedFields = string.Empty;
                AccountsController ctrlAccounts = new AccountsController();
                MethodInfo methodInfo = null;
                methodInfo = typeof(AccountsController).GetMethod(svcWorkflow.TransitionFunction);

                if (methodInfo != null)
                {
                    log.Info("get parameter for method");
                    ParameterInfo[] parameters = methodInfo?.GetParameters();
                    if (parameters == null)
                    {
                        log.Info(string.Format("no parameters found. invoke method"));
                        methodInfo?.Invoke(type, null);
                        return Response<object>.CreateResponse(Constants.Failed, null, null);
                    }
                    else
                    {
                        log.Info(string.Format("parameters found"));
                        object classInstance = Activator.CreateInstance(type, null);
                        AccountsController ctrlAccount = new AccountsController();

                        if (svcWorkflow.TransitionFunction == "PrintLoanStatement")
                        {
                            try
                            {
                                log.Info("execute fn_printLoanStatement");
                                var byteArray = ctrlAccount.PrintLoanStatement(Convert.ToInt32(id));


                                if (byteArray == null)
                                {
                                    return Response<object>.CreateResponse(Constants.Failed, null, new ErrorHandler() { ErrorCode = Constants.ActionsErrorCode, Message = Constants.PrintQuoteExecutionErrorMessage });
                                }
                                else
                                {
                                    return Response<object>.CreateResponse(Constants.Success, byteArray, null);
                                }
                            }
                            catch (Exception ex)
                            {
                                log.Error(string.Format("Error executing fn_printquote: id {0}, action {1}, category {2}\nError: {3}", id, action, category, ex));
                                throw;
                            }
                        }


                        else if (svcWorkflow.TransitionFunction == "PrintPaidUpLetter")
                        {
                            try
                            {
                                log.Info("execute fn_printagree");
                                var byteArray = ctrlAccount.PrintPaidUpLetter(Convert.ToInt32(id));

                                if (byteArray == null)
                                {
                                    return Response<object>.CreateResponse(Constants.Failed, null, new ErrorHandler() { ErrorCode = Constants.ActionsErrorCode, Message = Constants.PrintQuoteExecutionErrorMessage });
                                }
                                else
                                {
                                    return Response<object>.CreateResponse(Constants.Success, byteArray, null);
                                }
                            }
                            catch (Exception ex)
                            {
                                log.Error(string.Format("Error executing fn_printquote: id {0}, action {1}, category {2}\nError: {3}", id, action, category, ex));
                                throw;
                            }
                        }
                    }
                }
                log.Info(string.Format("Transition function {0} not found. id {1}, action {2}, category {3}", svcWorkflow.TransitionFunction, id, action, category));
                return Response<object>.CreateResponse(Constants.Failed, null, new ErrorHandler() { ErrorCode = Constants.ActionsErrorCode, Message = Constants.ActionsInvalidMessage });
            }
            catch (Exception ex)
            {
                log.Error(string.Format("Error invoking account action: id {0}, action {1}, category {2}\nError: {3}", id, action, category, ex));
                throw;
            }
        }

        /// <summary>
        /// This method invokes the said action to be performed on the application object
        /// </summary>
        /// <param name="svcWorkflow">workflow service object that determines the next state of the object when action is performed</param>
        /// <param name="id">id of the object</param>
        /// <param name="action">action to be performed</param>
        /// <param name="data">paramaters, if any</param>
        private Response<object> InvokeApplicationActions(BOS_WorkFlowService svcWorkflow, int id, string category, string action, string data, string api, string documentId)
        {
            try
            {
                log.Info(string.Format("Invoke application action: id {0}, action {1}, category {2}", id, action, category));
                var type = Type.GetType(typeof(ApplicationController).ToString());
                string editedFields = string.Empty;
                string validation = string.Empty;
                ApplicationController ctrlApplication = new ApplicationController();

                log.Info("get parameter for method");
                MethodInfo methodInfo = null;
                methodInfo = typeof(ApplicationController).GetMethod(svcWorkflow.TransitionFunction);

                if (methodInfo != null)
                {
                    log.Info(string.Format("no parameters found. invoke method"));
                    ParameterInfo[] parameters = methodInfo?.GetParameters();
                    if (parameters == null)
                    {
                        methodInfo?.Invoke(type, null);
                        return Response<object>.CreateResponse(Constants.Failed, null, null);
                    }
                    else
                    {
                        log.Info(string.Format("parameters found"));
                        object classInstance = Activator.CreateInstance(type, null);
                        ApplicationController ctrlApp = new ApplicationController();

                        if (category == BO_ObjectAPI.client.ToString())
                        {
                            log.Info("category: client");
                            if (svcWorkflow.TransitionFunction.ToLower() == "editapplicationclient")
                            {
                                try
                                {
                                    log.Info("Edit application client details");
                                    Atlas.Online.Data.Models.DTO.VMApplications editapplicationclient = new Atlas.Online.Data.Models.DTO.VMApplications();
                                    editapplicationclient = JsonConvert.DeserializeObject<Atlas.Online.Data.Models.DTO.VMApplications>(data);
                                    int appId = Convert.ToInt32(id);
                                    Response<string> editDetails = ctrlApp.EditApplicationClient(appId, editapplicationclient);
                                    editedFields = editDetails.Data;
                                }
                                catch (Exception ex)
                                {
                                    log.Error(string.Format("Error editing application client details: id {0}, action {1}, category {2}\nError: {3}", id, action, category, ex));
                                    throw;
                                }
                            }
                            ctrlApp.UpdateApplicationClientStatus(id, (int)svcWorkflow.NewStatus.StatusId, action, data, editedFields, BO_ObjectAPI.client);
                        }
                        else if (category == BO_ObjectAPI.bankdetails.ToString().ToLower())
                        {
                            log.Info("category: bank details");
                            if (svcWorkflow.TransitionFunction.ToLower() == "updateapplicationbankdetails")
                            {
                                try
                                {
                                    log.Info("Edit application bank details");
                                    BankDetailDto bankdetailsDTO = new BankDetailDto();
                                    bankdetailsDTO = JsonConvert.DeserializeObject<BankDetailDto>(data);
                                    int appId = Convert.ToInt32(id);
                                    Response<string> editDetails = ctrlApp.UpdateApplicationBankDetails(bankdetailsDTO, appId, (int)svcWorkflow.NewStatus.StatusId);
                                    editedFields = editDetails.Data;
                                }
                                catch (Exception ex)
                                {
                                    log.Error(string.Format("Error editing application bank details: id {0}, action {1}, category {2}\nError: {3}", id, action, category, ex));
                                    throw;
                                }
                            }
                            else if (svcWorkflow.TransitionFunction.ToLower() == "fn_checkdigit")
                            {
                                try
                                {
                                    log.Info("Execute fn_checkdigit action");
                                    var result = ctrlApp.fn_CheckDigit(id);
                                    if (result.Status.ToLower() != "success")
                                        return Response<object>.CreateResponse(Constants.Failed, null, new ErrorHandler() { ErrorCode = result.Error.ErrorCode, Message = result.Error.Message });
                                }
                                catch (Exception ex)
                                {
                                    log.Error(string.Format("Error executing fn_checkdigit action: id {0}, action {1}, category {2}\nError: {3}", id, action, category, ex));
                                    throw;
                                }
                            }
                            else if (svcWorkflow.TransitionFunction.ToLower() == "fn_AVSCheckEasyDebit".ToLower())
                            {
                                try
                                {
                                    log.Info("Execute fn_avscheck action");
                                    var result = ctrlApp.fn_AVSCheckEasyDebit(id).Result;
                                    if (result.Status.ToLower() != "success")
                                        return Response<object>.CreateResponse(Constants.Failed, null, new ErrorHandler() { ErrorCode = result.Error.ErrorCode, Message = result.Error.Message });
                                }
                                catch (Exception ex)
                                {
                                    log.Error(string.Format("Error executing fn_AVSCheckEasyDebit action: id {0}, action {1}, category {2}\nError: {3}", id, action, category, ex));
                                    throw;
                                }
                            }
                            else if (svcWorkflow.TransitionFunction.ToLower() == "fn_AVSCheckNuPay".ToLower())
                            {
                                try
                                {
                                    log.Info("Execute fn_avscheck action");
                                    var result = ctrlApp.fn_AVSCheckNuPay(id);
                                    if (result.Status.ToLower() != "success")
                                        return Response<object>.CreateResponse(Constants.Failed, null, new ErrorHandler() { ErrorCode = result.Error.ErrorCode, Message = result.Error.Message });
                                }
                                catch (Exception ex)
                                {
                                    log.Error(string.Format("Error executing fn_AVSCheckNuPay action: id {0}, action {1}, category {2}\nError: {3}", id, action, category, ex));
                                    throw;
                                }
                            }
                            ctrlApp.UpdateApplicationBankStatus(id, (int)svcWorkflow.NewStatus.StatusId, action, data, editedFields, BO_ObjectAPI.bankdetails);
                        }
                        else if (category == BO_ObjectAPI.employerdetails.ToString().ToLower())
                        {
                            log.Info("category: employer");
                            if (svcWorkflow.TransitionFunction.ToLower() == "addupdateapplicationemployer")
                            {
                                try
                                {
                                    EmployerDTO emp = new EmployerDTO();
                                    emp = JsonConvert.DeserializeObject<EmployerDTO>(data);
                                    Response<string> editDetails = ctrlApp.AddUpdateApplicationEmployer(id, emp, (int)svcWorkflow.NewStatus.StatusId);

                                    if (editDetails.Status == Constants.Failed)
                                    {
                                        log.Error(string.Format("Error adding/updating employer details : id {0}, action {1}, category {2}", id, action, category));
                                        return Response<object>.CreateResponse(Constants.Failed, null, new ErrorHandler() { ErrorCode = Constants.ApplicationErrorCode, Message = Constants.UpdateEmployerErrorMessage });
                                    }
                                    editedFields = editDetails.Data;
                                }
                                catch (Exception ex)
                                {
                                    log.Error(string.Format("Error editing application employer details: id {0}, action {1}, category {2}\nError: {3}", id, action, category, ex));
                                    throw;
                                }
                            }
                            else if (svcWorkflow.TransitionFunction == "VerifyEmployer")
                            {
                                try
                                {
                                    EmployerDTO emp = new EmployerDTO();
                                    emp = JsonConvert.DeserializeObject<EmployerDTO>(data);
                                    ctrlApp.VerifyEmployer(id, emp);
                                }
                                catch (Exception ex)
                                {
                                    log.Error(string.Format("Error editing application employer details: id {0}, action {1}, category {2}\nError: {3}", id, action, category, ex));
                                    throw;
                                }
                            }
                            else if (svcWorkflow.TransitionFunction == "RejectEmployer")
                            {
                                try
                                {
                                    EmployerDTO emp = new EmployerDTO();
                                    emp = JsonConvert.DeserializeObject<EmployerDTO>(data);
                                    ctrlApp.RejectEmployer(id, emp.Comment);
                                }
                                catch (Exception ex)
                                {
                                    log.Error(string.Format("Error editing application employer details: id {0}, action {1}, category {2}\nError: {3}", id, action, category, ex));
                                    throw;
                                }
                            }

                            ctrlApp.UpdateApplicationEmployerDetails(id, (int)svcWorkflow.NewStatus.StatusId, action, data, editedFields, BO_ObjectAPI.employerdetails);
                        }
                        else if (category == BO_ObjectAPI.affordability.ToString().ToLower())
                        {
                            log.Info("category: affordability");
                            if (svcWorkflow.TransitionFunction.ToLower() == "editaffordability")
                            {
                                try
                                {
                                    log.Info("edit affordability");
                                    string quotationResult = null;
                                    AffordabilityDto affordabilityDto = new AffordabilityDto();
                                    affordabilityDto = JsonConvert.DeserializeObject<AffordabilityDto>(data);
                                    var result = ctrlApplication.EditAffordability(affordabilityDto, id, (int)svcWorkflow.NewStatus.StatusId);
                                    if (result.Status.ToLower() != Constants.Success.ToLower())
                                        return Response<object>.CreateResponse(Constants.Failed, null, result.Error);
                                    else
                                    {
                                        editedFields = result.Data;
                                        affordabilityDto.GenerateQuotation.SelectedInterestRate = affordabilityDto.GenerateQuotation.AnnualRateOfInterest / 12;
                                        GenerateQuotation GenerateQuote = affordabilityDto?.GenerateQuotation;
                                        if (GenerateQuote.SelectedLoanAmt > 0 && GenerateQuote.SelectedTerm > 0)
                                        {
                                            quotationResult = ctrlApplication.generatequotation(GenerateQuote, id, out validation);
                                            if (string.IsNullOrEmpty(quotationResult))
                                            {
                                                if (validation != null && validation != "")
                                                {
                                                    return Response<object>.CreateResponse(Constants.Failed, null, new ErrorHandler() { ErrorCode = validation, Message = validation });
                                                }
                                                return Response<object>.CreateResponse(Constants.Failed, null, new ErrorHandler() { ErrorCode = Constants.ActionsErrorCode, Message = Constants.QuotePayLoadNull });
                                            }
                                            ApplicationCaseUtil.UpdateApplicatinCaseState(id, ApplicationCaseObject.GENERATE_QUOTATION.ToString());

                                        }
                                    }
                                }
                                catch (Exception ex)
                                {
                                    log.Error(string.Format("Error editing application affordability: id {0}, action {1}, category {2}\nError: {3}", id, action, category, ex));
                                    throw;
                                }
                            }
                            if (svcWorkflow.TransitionFunction.ToLower() == "generatequotation")
                            {
                                try
                                {
                                    log.Info("Edit quotation");
                                    AffordabilityDto affordabilityDto = new AffordabilityDto();
                                    affordabilityDto = JsonConvert.DeserializeObject<AffordabilityDto>(data);

                                    //first step - save affordability
                                    using (var uow = new UnitOfWork())
                                    {
                                        long affordabilitystatus = 0;
                                        affordabilitystatus = new XPQuery<BOS_WorkFlowService>(uow).Where(x => x.Object.ObjectName.ToLower() == svcWorkflow.Object.ObjectName.ToLower() && x.Action.ActionName.ToLower() == BackOfficeEnum.Action.EDIT.ToString().ToLower() && x.API.ToLower() == api && x.Status.StatusId == svcWorkflow.Status.StatusId).Select(s => s.NewStatus.StatusId).FirstOrDefault();
                                        var result1 = ctrlApplication.EditAffordability(affordabilityDto, (int)id, (int)affordabilitystatus);
                                        if (result1.Status.ToLower() != Constants.Success.ToLower())
                                            return Response<object>.CreateResponse(Constants.Failed, null, result1.Error);
                                    }
                                    //second step - generate quote

                                    //check if all items in checklist are verified
                                    if (!ctrlApp.IsChecklistComplete(id))
                                    {
                                        log.Info("Checklist items pending");
                                        return Response<object>.CreateResponse(Constants.Failed, null, new ErrorHandler() { ErrorCode = Constants.ApplicationErrorCode, Message = Constants.ChecklistItemsPendingError });
                                    }

                                    GenerateQuotation GenerateQuote = affordabilityDto != null ? affordabilityDto.GenerateQuotation : null;
                                    GenerateQuote.SelectedInterestRate = GenerateQuote.AnnualRateOfInterest / 12;
                                    string result = ctrlApplication.generatequotation(GenerateQuote, id, out validation);
                                    if (validation != null && validation != "")
                                    {
                                        return Response<object>.CreateResponse(Constants.Failed, null, new ErrorHandler() { ErrorCode = validation, Message = validation });
                                    }
                                    if (!string.IsNullOrEmpty(result))
                                    {
                                        ApplicationCaseUtil.UpdateApplicatinCaseState(id, ApplicationCaseObject.GENERATE_QUOTATION.ToString());
                                    }
                                    editedFields = result;
                                }
                                catch (Exception ex)
                                {
                                    log.Error(string.Format("Error editing application quotation: id {0}, action {1}, category {2}\nError: {3}", id, action, category, ex));
                                    throw;
                                }
                            }
                            else if (svcWorkflow.TransitionFunction.ToLower() == "fn_getscorecard")
                            {
                                try
                                {
                                    log.Info("execute fn_getscorecard");
                                    var result = ctrlApp.fn_GetScoreCard(id);
                                    if (result.Status.ToLower() != "success")
                                        return Response<object>.CreateResponse(Constants.Failed, null, new ErrorHandler() { ErrorCode = result.Error.ErrorCode, Message = result.Error.Message });
                                }
                                catch (Exception ex)
                                {
                                    log.Error(string.Format("Error executing fn_getscorecard: id {0}, action {1}, category {2}\nError: {3}", id, action, category, ex));
                                    throw;
                                }
                            }
                            else if (svcWorkflow.TransitionFunction.ToLower() == "RejectApplication".ToLower())
                            {
                                try
                                {
                                    log.Info("execute RejectApplication");
                                    ViewModels.VMApplications json = JsonConvert.DeserializeObject<ViewModels.VMApplications>(data);
                                    var result = ctrlApp.RejectApplication(id, json.Comment, BO_ObjectAPI.affordability.ToString());
                                    if (result.Status.ToLower() != "success")
                                        return Response<object>.CreateResponse(Constants.Failed, null, new ErrorHandler() { ErrorCode = result.Error.ErrorCode, Message = result.Error.Message });
                                }
                                catch (Exception ex)
                                {
                                    log.Error(string.Format("Error executing RejectApplication: id {0}, action {1}, category {2}\nError: {3}", id, action, category, ex));
                                    throw;
                                }
                            }
                            ctrlApplication.UpdateApplicationAffordabilityStatus(id, (int)svcWorkflow.NewStatus.StatusId, action, data, editedFields, BO_ObjectAPI.affordability);
                        }
                        else if (category == BO_ObjectAPI.disbursement.ToString().ToLower())
                        {
                            log.Info("category: disbursement");
                            if (svcWorkflow.TransitionFunction.ToLower() == "editdisbursement")
                            {
                                log.Info("Edit disbursement");
                                try
                                {
                                    var lastStatus = svcWorkflow.Status.StatusId;
                                    DisbursementDto disbursementDto = new DisbursementDto();
                                    disbursementDto = JsonConvert.DeserializeObject<DisbursementDto>(data);
                                    var result = ctrlApplication.EditDisbursement(disbursementDto, id, (int)svcWorkflow.NewStatus.StatusId);
                                    editedFields = result.Data;
                                    if (result.Status == Constants.Failed)
                                        return Response<object>.CreateResponse(Constants.Failed, null, result.Error);
                                }
                                catch (Exception ex)
                                {
                                    log.Error(string.Format("Error editing disbusement: id {0}, action {1}, category {2}\nError: {3}", id, action, category, ex));
                                    throw;
                                }
                            }
                            else if (svcWorkflow.TransitionFunction.ToLower() == "fn_printAgree")
                            {
                                try
                                {
                                    log.Info("execute fn_printagree");
                                    var byteArray = ctrlApplication.fn_printAgree(id);

                                    if (byteArray == null)
                                        return Response<object>.CreateResponse(Constants.Failed, null, new ErrorHandler() { ErrorCode = Constants.ActionsErrorCode, Message = Constants.PrintQuoteExecutionErrorMessage });
                                }
                                catch (Exception ex)
                                {
                                    log.Error(string.Format("Error executing fn_printquote: id {0}, action {1}, category {2}\nError: {3}", id, action, category, ex));
                                    throw;
                                }
                            }
                            else if (svcWorkflow.TransitionFunction.ToLower() == "fn_loadnucard")
                            {
                                try
                                {
                                    log.Info("execute fn_loadnucard");
                                    var obj = new ApplicationDto();

                                    using (var result = new BackOfficeWebServiceClient("BackOfficeWebServer.NET"))
                                    {
                                        try
                                        {
                                            log.Info("get filters");
                                            var filterConditions = FilterUtil.GetFiltersAsString(BO_Object.Applications, Convert.ToInt32(HttpContext.Current.Session["UserId"]));
                                            obj = result.GetApplicationDetail(filterConditions, id);
                                        }
                                        catch (Exception ex)
                                        {
                                            log.Error(string.Format("Error getting filters: id {0}, action {1}, category {2}\nError: {3}", id, action, category, ex));
                                            throw;
                                        }
                                    }
                                    if (obj != null)
                                    {
                                        if (ctrlApp.IsChecklistComplete(obj.ApplicationId))
                                        {
                                            log.Info("get nu card number");
                                            documentId = obj.Disbursement.NuCardNumber;
                                        }
                                        else
                                        {
                                            log.Info("Checklist items pending");
                                            return Response<object>.CreateResponse(Constants.Failed, null, new ErrorHandler() { ErrorCode = Constants.ApplicationErrorCode, Message = Constants.ChecklistItemsPendingError });
                                        }
                                    }

                                    if (!string.IsNullOrEmpty(documentId))
                                    {
                                        log.Info("check card: execute fn_IssueNuCard");
                                        var checkCard = ctrlApplication.fn_IssueNuCard(id, documentId);

                                        if (checkCard.Data != null)
                                        {
                                            var resultText = checkCard.Data.methodResponse.@params.param.value.@struct.member.Where(a => a.name == "resultText").Select(a => a.value.@string).FirstOrDefault();
                                            if (resultText.ToLower() == "ok")// || resultText.ToLower() == "card already allocated")
                                            {
                                                //using (var uow = new UnitOfWork())
                                                //{
                                                //    var updateData = new XPQuery<NUC_NuCard>(uow)?.Where(a => a.SequenceNum == documentId).FirstOrDefault();
                                                //    if (updateData != null)
                                                //    {
                                                //        updateData.Status = new XPQuery<NUC_NuCardStatus>(uow)?.Where(a => a.NuCardStatusId == (int)Atlas.Enumerators.NuCard.NuCardStatus.ISSUE).FirstOrDefault();
                                                //        uow.Save(updateData);
                                                //        uow.CommitChanges();
                                                //    }
                                                //}

                                                var accountId = ctrlApplication.Disbursement(id);
                                                if (accountId != 0)
                                                {
                                                    try
                                                    {
                                                        var result = ctrlApp.fn_LoadNuCard(id, documentId);
                                                        if (result.Status != Constants.Success)
                                                        {
                                                            return Response<object>.CreateResponse(Constants.Failed, null, result.Error);
                                                        }

                                                    }
                                                    catch (Exception ex)
                                                    {
                                                        log.Error(ex.ToString());
                                                        return Response<object>.CreateResponse(Constants.Failed, null, new ErrorHandler() { ErrorCode = Constants.FailedCode, Message = ex.Message });
                                                    }
                                                }
                                                else
                                                    return Response<object>.CreateResponse(Constants.Failed, null, new ErrorHandler() { ErrorCode = Constants.ActionsErrorCode, Message = Constants.AccountDetailsNotFound });
                                            }
                                            else
                                                return Response<object>.CreateResponse(Constants.Failed, null, new ErrorHandler() { ErrorCode = checkCard.Data.methodResponse.@params.param.value.@struct.member.Where(a => a.name == "resultCode").Select(a => a.value.@int).FirstOrDefault(), Message = checkCard.Data.methodResponse.@params.param.value.@struct.member.Where(a => a.name == "resultText").Select(a => a.value.@string).FirstOrDefault() });
                                        }
                                        else
                                            return Response<object>.CreateResponse(Constants.Failed, null, new ErrorHandler() { ErrorCode = checkCard.Error.ErrorCode, Message = checkCard.Error.Message });
                                    }
                                    else
                                    {
                                        return Response<object>.CreateResponse(Constants.Failed, null, new ErrorHandler() { ErrorCode = Constants.ActionsErrorCode, Message = Constants.VoucherNumber });
                                    }
                                }
                                catch (Exception ex)
                                {
                                    log.Error(string.Format("Error executing fn_loadnucard: id {0}, action {1}, category {2}\nError: {3}", id, action, category, ex));
                                    throw;
                                }
                            }

                            else if (svcWorkflow.TransitionFunction.ToLower() == "fn_handshake")
                            {
                                var result = ctrlApp.fn_Handshake();
                            }
                            else if (svcWorkflow.TransitionFunction.ToLower() == "fn_tranidquery")
                            {
                                var result = ctrlApp.fn_tranIDQuery(data);
                            }
                            else if (svcWorkflow.TransitionFunction.ToLower() == "fn_performcardswipe")
                            {
                                var result = ctrlApp.fn_PerformCardSwipe();
                            }
                            else if (svcWorkflow.TransitionFunction.ToLower() == "fn_createdebitorder")
                            {
                                try
                                {
                                    log.Info("execute fn_createdebitorder");
                                    var result = ctrlApp.fn_CreateDebitOrder(id);
                                    if (result.Status != Constants.Success)
                                        return Response<object>.CreateResponse(Constants.Failed, null, new ErrorHandler() { ErrorCode = result.Error.ErrorCode, Message = result.Error.Message });
                                }
                                catch (Exception ex)
                                {
                                    log.Error(string.Format("Error executing fn_createdebitorder: id {0}, action {1}, category {2}\nError: {3}", id, action, category, ex));
                                    throw;
                                }
                            }
                            else if (svcWorkflow.TransitionFunction.ToLower() == "allocatenewcard")
                            {
                                try
                                {
                                    log.Info("execute fn_createdebitorder");
                                    DisbursementDto disbursementDto = new DisbursementDto();
                                    disbursementDto = JsonConvert.DeserializeObject<DisbursementDto>(data);
                                    var app = ctrlApp.GetApplicationById(id).Data;
                                    var result = new Response<string>();
                                    if (app.ApplicationDetail.Disbursement.CardDetails != null && app.ApplicationDetail.Disbursement.CardDetails.ReasonId > 0)
                                        result = ctrlApp.AllocateNewCard(id, app.ApplicationDetail.Disbursement.CardDetails.ReasonId);
                                    else
                                        result = ctrlApp.AllocateNewCard(id);
                                    if (result.Status != Constants.Success)
                                        return Response<object>.CreateResponse(Constants.Failed, null, new ErrorHandler() { ErrorCode = result.Error.ErrorCode, Message = result.Error.Message });
                                }
                                catch (Exception ex)
                                {
                                    log.Error(string.Format("Error executing fn_createdebitorder: id {0}, action {1}, category {2}\nError: {3}", id, action, category, ex));
                                    throw;
                                }
                            }

                            else if (svcWorkflow.TransitionFunction.ToLower() == "fn_issuenucard")
                            {
                                try
                                {
                                    log.Info("execute fn_issuenucard");
                                    var json = JsonConvert.DeserializeObject<Integrations.NuCardDetails>(data);
                                    documentId = json.NuCardNumber;
                                    if (documentId != "")
                                    {
                                        var result = ctrlApplication.fn_IssueNuCard(id, documentId);
                                        var returnData = JsonConvert.SerializeObject(result);
                                    }
                                    else
                                    {
                                        return Response<object>.CreateResponse(Constants.Failed, null, new ErrorHandler() { ErrorCode = Constants.ActionsErrorCode, Message = Constants.VoucherNumber });
                                    }
                                }
                                catch (Exception ex)
                                {
                                    log.Error(string.Format("Error executing fn_issuenucard: id {0}, action {1}, category {2}\nError: {3}", id, action, category, ex));
                                    throw;
                                }
                            }
                            else if (svcWorkflow.TransitionFunction == "UpdateApplicationDisbursementStatus" && action.ToUpper() == "APPLICATION_CAPTURE_CONSULTANT_FINGER_PRINT")
                            {
                                try
                                {
                                    //check if all items in checklist are verified
                                    if (ctrlApp.IsChecklistComplete(id))
                                    {
                                        if (System.Configuration.ConfigurationManager.AppSettings["fingerPrintbypass"] == "false")
                                        {
                                            log.Info("Execute verify enrollement for consultant. Mock fingerPrintbypass is false.");
                                            var lastStatus = svcWorkflow.Status.StatusId;
                                            DisbursementDto disbursementDto = new DisbursementDto();
                                            disbursementDto = JsonConvert.DeserializeObject<DisbursementDto>(data);
                                            var result = ctrlApplication.EditDisbursement(disbursementDto, id, (int)svcWorkflow.NewStatus.StatusId);
                                            editedFields = result.Data;
                                            if (result.Status == Constants.Failed)
                                                return Response<object>.CreateResponse(Constants.Failed, null, result.Error);

                                            var staffPersonId = Convert.ToInt64(HttpContext.Current.Session["PersonId"]);
                                            using (var uow = new UnitOfWork())
                                            {
                                                var person = new XPQuery<PER_Person>(uow).Where(x => x.PersonId == staffPersonId).FirstOrDefault();
                                                var verifyConsultant = ctrlApp.VerifyEnrolment(id, "CONSULTANT", person.ASSPersonId);
                                                if (verifyConsultant.Status == Constants.Failed)
                                                    return Response<object>.CreateResponse(Constants.Failed, verifyConsultant.Data, new ErrorHandler() { ErrorCode = verifyConsultant.Error.ErrorCode, Message = verifyConsultant.Error.Message });
                                            }
                                        }
                                    }
                                    else
                                    {
                                        log.Info("Checklist items pending");
                                        return Response<object>.CreateResponse(Constants.Failed, null, new ErrorHandler() { ErrorCode = Constants.ApplicationErrorCode, Message = Constants.ChecklistItemsPendingError });
                                    }
                                }
                                catch (Exception ex)
                                {
                                    log.Error(string.Format("Error executing verify enrollement for customer: id {0}, action {1}, category {2}\nError: {3}", id, action, category, ex));
                                    throw;
                                }
                            }
                            else if (svcWorkflow.TransitionFunction == "UpdateApplicationDisbursementStatus" && action.ToUpper() == "APPLICATION_CAPTURE_BRANCH_MANAGER_FINGER_PRINT")
                            {
                                try
                                {
                                    VMApplicationHistory appHistory = new VMApplicationHistory();
                                    using (UnitOfWork uow = new UnitOfWork())
                                    {
                                        //  TODO : Remove Action 20 hard coded value
                                        var history = new XPQuery<BOS_ApplicationEventHistory>(uow).FirstOrDefault(h => h.ApplicationId == id && h.Category.ToLower() == "disbursement" && h.Action.ActionId == 20);
                                        if (history != null)
                                        {
                                            var userId = Convert.ToInt64(HttpContext.Current.Session["UserId"]);
                                            if (history.User.UserId == userId)
                                            {
                                                return Response<object>.CreateResponse(Constants.Failed, null, new ErrorHandler() { ErrorCode = Constants.ApplicationErrorCode, Message = "Verifier cannot approve application" });
                                            }
                                        }
                                    }

                                    //check if all items in checklist are verified
                                    if (ctrlApp.IsChecklistComplete(id))
                                    {
                                        if (System.Configuration.ConfigurationManager.AppSettings["fingerPrintbypass"] == "false")
                                        {
                                            log.Info("Execute verify enrollement for manager. Mock fingerPrintbypass is false.");
                                            var staffPersonId = Convert.ToInt64(HttpContext.Current.Session["PersonId"]);
                                            using (var uow = new UnitOfWork())
                                            {
                                                var person = new XPQuery<PER_Person>(uow).Where(x => x.PersonId == staffPersonId).FirstOrDefault();
                                                var verifyConsultant = ctrlApp.VerifyEnrolment(id, "BRANCH_MANAGER", person.ASSPersonId);
                                                if (verifyConsultant.Status == Constants.Failed)
                                                    return Response<object>.CreateResponse(Constants.Failed, verifyConsultant.Data, new ErrorHandler() { ErrorCode = verifyConsultant.Error.ErrorCode, Message = verifyConsultant.Error.Message });
                                            }
                                        }
                                        //disable edit action for personal, employer and bank
                                        var app = new ApplicationController();
                                        app.UpdateApplicationClientStatus(id, 3, "APPLICATION_CAPTURE_BRANCH_MANAGER_FINGER_PRINT", data, editedFields, BO_ObjectAPI.client);
                                        app.UpdateApplicationBankStatus(id, 3, "APPLICATION_CAPTURE_BRANCH_MANAGER_FINGER_PRINT", data, editedFields, BO_ObjectAPI.bankdetails);
                                        app.UpdateApplicationEmployerDetails(id, 3, "APPLICATION_CAPTURE_BRANCH_MANAGER_FINGER_PRINT", data, editedFields, BO_ObjectAPI.employerdetails);
                                    }
                                    else
                                    {
                                        log.Info("Checklist items pending");
                                        return Response<object>.CreateResponse(Constants.Failed, null, new ErrorHandler() { ErrorCode = Constants.ApplicationErrorCode, Message = Constants.ChecklistItemsPendingError });
                                    }

                                }
                                catch (Exception ex)
                                {
                                    log.Error(string.Format("Error executing verify enrollement for manager: id {0}, action {1}, category {2}\nError: {3}", id, action, category, ex));
                                    throw;
                                }
                            }

                            else if (svcWorkflow.TransitionFunction == "fn_DisburseWithABSA" && action.ToUpper() == "APPLICATION_DISBURSE")
                            {
                                ctrlApp.fn_DisburseWithABSA(id);
                                var accountId = ctrlApplication.Disbursement(id);

                            }
                            else if (svcWorkflow.TransitionFunction.ToLower() == "fn_CreateDebicheckDebitOrder".ToLower() && action.ToUpper() == "APPLICATION_CREATE_DEBIT_ORDER")
                            {
                                try
                                {
                                    log.Info("execute fn_CreateDebicheckDebitOrder");
                                    var result = ctrlApp.fn_CreateDebicheckDebitOrder(id).Result;
                                    if (result.Status != Constants.Success)
                                    {

                                        return Response<object>.CreateResponse(Constants.Failed, null, new ErrorHandler() { ErrorCode = "", Message = "" });
                                    }
                                }
                                catch (Exception ex)
                                {
                                    log.Error(string.Format("Error executing fn_CreateDebicheckDebitOrder: id {0}, action {1}, category {2}\nError: {3}", id, action, category, ex));
                                    return Response<object>.CreateResponse(Constants.Failed, null, new ErrorHandler() { ErrorCode = "", Message = ex.Message });

                                    //throw;
                                }
                            }

                            ctrlApplication.UpdateApplicationDisbursementStatus(id, (int)svcWorkflow.NewStatus.StatusId, action, data, editedFields, BO_ObjectAPI.disbursement);

                        }
                        else if (category == BO_ObjectAPI.quotation.ToString().ToLower())
                        {
                            log.Info("category: quotation");
                            if (svcWorkflow.TransitionFunction.ToLower() == "fn_performsignflow")
                            {
                                try
                                {
                                    log.Info("Execute fn_performsignflow");
                                    var result = ctrlApplication.fn_PerformSignFlow(id, data);
                                    if (result.Status != "Success")
                                        return Response<object>.CreateResponse(Constants.Failed, null, new ErrorHandler() { ErrorCode = Constants.ActionsErrorCode, Message = Constants.SignFlowExecutionErrorMessage });
                                }
                                catch (Exception ex)
                                {
                                    log.Error(string.Format("Error executing fn_performsignflow: id {0}, action {1}, category {2}\nError: {3}", id, action, category, ex));
                                    throw;
                                }
                            }
                            else if (svcWorkflow.TransitionFunction.ToLower() == "fn_printquote")
                            {
                                try
                                {
                                    log.Info("execute fn_printquote");
                                    var byteArray = ctrlApplication.fn_PrintQuote(id);
                                    if (byteArray == null)
                                        return Response<object>.CreateResponse(Constants.Failed, null, new ErrorHandler() { ErrorCode = Constants.ActionsErrorCode, Message = Constants.PrintQuoteExecutionErrorMessage });
                                }
                                catch (Exception ex)
                                {
                                    log.Error(string.Format("Error executing fn_printquote: id {0}, action {1}, category {2}\nError: {3}", id, action, category, ex));
                                    throw;
                                }
                            }
                            else if (svcWorkflow.TransitionFunction.ToLower() == "generateotp")
                            {
                                log.Info("Generate OTP");
                                var generateOTP = ctrlApp.GenerateOTP(BO_Object.Applications.ToString(), id, category, Convert.ToInt32(documentId));
                                if (generateOTP.Status == Constants.Failed)
                                    return Response<object>.CreateResponse(Constants.Failed, null, new ErrorHandler() { ErrorCode = generateOTP.Error.ErrorCode, Message = generateOTP.Error.Message });
                            }
                            else if (svcWorkflow.TransitionFunction.ToLower() == "rejectquotation")
                            {
                                try
                                {
                                    log.Info("execute RejectApplication");
                                    ViewModels.VMApplications json = JsonConvert.DeserializeObject<ViewModels.VMApplications>(data);
                                    var result = ctrlApp.RejectApplication(id, json.Comment, BO_ObjectAPI.quotation.ToString());
                                    if (result.Status.ToLower() != "success")
                                        return Response<object>.CreateResponse(Constants.Failed, null, new ErrorHandler() { ErrorCode = result.Error.ErrorCode, Message = result.Error.Message });
                                }
                                catch (Exception ex)
                                {
                                    log.Error(string.Format("Error executing RejectApplication: id {0}, action {1}, category {2}\nError: {3}", id, action, category, ex));
                                    throw;
                                }
                            }
                            else if (svcWorkflow.TransitionFunction.ToLower() == "verifyotp")
                            {
                                Response<bool> result;
                                try
                                {
                                    log.Info("Execute verify otp");
                                    VMOTP json = JsonConvert.DeserializeObject<VMOTP>(data);
                                    var otp = (string.IsNullOrEmpty(Convert.ToString(json.OTP)) ? 0 : Convert.ToInt32(json.OTP));
                                    result = ctrlApp.VerifyOTP(id, json.SubObjectId, otp);
                                    if (!result.Data)
                                    {
                                        return Response<object>.CreateResponse(Constants.Failed, false, new ErrorHandler() { ErrorCode = Constants.ActionsErrorCode, Message = result.Error.Message });
                                    }
                                    //else
                                    //{
                                    //    var resultDebiCheck = ctrlApp.fn_CreateDebicheckDebitOrder(id);
                                    //    if (resultDebiCheck.Result.Status == Constants.Success)
                                    //    {
                                    //        ctrlApplication.UpdateApplicationDisbursementStatus(id, (int)BackOfficeEnum.NewStatus.STATUS_DEBIT_ORDER_APPROVED, action, data, editedFields, BO_ObjectAPI.disbursement);

                                    //        var resultDisburse = ctrlApp.fn_DisburseWithABSA(id);
                                    //        var accountId = ctrlApplication.Disbursement(id);
                                    //        var ffdisburse = ctrlApplication.UpdateFlowFinanceDisbursement(id, "AOL" + accountId.ToString().PadLeft(7, '0'));

                                    //        if (resultDisburse.Status == Constants.Success)
                                    //        {
                                    //            ctrlApplication.fn_printAgree(id);
                                    //            ctrlApplication.UpdateApplicationDisbursementStatus(id, (int)BackOfficeEnum.NewStatus.STATUS_DISBURSED, action, data, editedFields, BO_ObjectAPI.disbursement);
                                    //        }
                                    //        else
                                    //            log.Info(string.Format("Error while loan disbursement for : id {0}, action {1}, category {2}", id, action, category));
                                    //    }
                                    //    else
                                    //        log.Info(string.Format("Error while Debicheck debit order creation for: id {0}, action {1}, category {2}", id, action, category));
                                    //}

                                    try
                                    {
                                        log.Info("Execute fn_performsignflow");
                                        var signResult = ctrlApplication.fn_PerformSignFlow(id, data);
                                        if (signResult.Status != "Success")
                                            return Response<object>.CreateResponse(Constants.Failed, null, new ErrorHandler() { ErrorCode = Constants.ActionsErrorCode, Message = Constants.SignFlowExecutionErrorMessage });
                                    }
                                    catch (Exception ex)
                                    {
                                        log.Error(string.Format("Error executing fn_performsignflow: id {0}, action {1}, category {2}\nError: {3}", id, action, category, ex));
                                        throw;
                                    }
                                }
                                catch (Exception ex)
                                {
                                    log.Error(string.Format("Error executing verity otp: id {0}, action {1}, category {2}\nError: {3}", id, action, category, ex));
                                    throw;
                                }    
                            }
                            else if (svcWorkflow.TransitionFunction == "UpdateApplicationQuotationStatus" && action.ToUpper() == "APPLICATION_CAPTURE_CLIENT_FINGER_PRINT" && System.Configuration.ConfigurationManager.AppSettings["fingerPrintbypass"] == "false")
                            {
                                try
                                {
                                    log.Info("verify enrollment for client. Mock fingerPrintbypass is false.");
                                    var staffPersonId = Convert.ToInt64(HttpContext.Current.Session["PersonId"]);
                                    using (var uow = new UnitOfWork())
                                    {
                                        var person = new XPQuery<PER_Person>(uow).Where(x => x.PersonId == staffPersonId).FirstOrDefault();
                                        var verifyConsultant = ctrlApp.VerifyEnrolment(id, "CUSTOMER", person.ASSPersonId);

                                        if (verifyConsultant.Status == Constants.Failed)
                                            return Response<object>.CreateResponse(Constants.Failed, verifyConsultant.Data, new ErrorHandler() { ErrorCode = verifyConsultant.Error.ErrorCode, Message = verifyConsultant.Error.Message });
                                    }
                                }
                                catch (Exception ex)
                                {
                                    log.Error(string.Format("Error executing verify enrollment for client: id {0}, action {1}, category {2}\nError: {3}", id, action, category, ex));
                                    throw;
                                }
                            }
                            else if (svcWorkflow.TransitionFunction == "SendQuote")
                            {
                                //check if all items in checklist are verified
                                if (ctrlApp.IsChecklistComplete(id))
                                {
                                    var result = ctrlApp.SendQuote(id);
                                }
                                else
                                {
                                    log.Info("Checklist items pending");
                                    return Response<object>.CreateResponse(Constants.Failed, null, new ErrorHandler() { ErrorCode = Constants.ApplicationErrorCode, Message = Constants.ChecklistItemsPendingError });
                                }
                            }
                            //else if (svcWorkflow.TransitionFunction == "RejectEmployer")
                            //{
                            //    ctrlApp.RejectEmployer(id, "RejectReason:Rejected");
                            //}
                            //else if (svcWorkflow.TransitionFunction == "RejectApplication")
                            //{
                            //    ctrlApp.RejectApplication(id);
                            //}
                            else if (svcWorkflow.TransitionFunction == "ApproveAndSendQuote")
                            {
                                //check if all items in checklist are verified
                                if (!ctrlApp.IsChecklistComplete(id))
                                {
                                    log.Info("Checklist items pending");
                                    return Response<object>.CreateResponse(Constants.Failed, null, new ErrorHandler() { ErrorCode = Constants.ApplicationErrorCode, Message = Constants.ChecklistItemsPendingError });
                                }

                                using (UnitOfWork uow = new UnitOfWork())
                                {
                                    var history = new XPQuery<BOS_ApplicationEventHistory>(uow).FirstOrDefault(h => h.ApplicationId == id && h.Category.ToLower() == BO_ObjectAPI.quotation.ToString().ToLower() && h.Action.ActionId == (int)BackOfficeEnum.Action.APPLICATION_SEND_QUOTE);
                                    if (history != null)
                                    {
                                        var userId = Convert.ToInt64(HttpContext.Current.Session["UserId"]);
                                        if (history.User.UserId == userId)
                                        {
                                            return Response<object>.CreateResponse(Constants.Failed, null, new ErrorHandler() { ErrorCode = Constants.ApplicationErrorCode, Message = "Verifier cannot approve application" });
                                        }
                                    }
                                }

                                ctrlApp.ApproveAndSendQuote(id);
                            }
                            else if (svcWorkflow.Action.ActionId == (int)BackOfficeEnum.Action.APPLICATION_SEND_QUOTE)
                            {
                                //check if all items in checklist are verified
                                if (!ctrlApp.IsChecklistComplete(id))
                                {
                                    log.Info("Checklist items pending");
                                    return Response<object>.CreateResponse(Constants.Failed, null, new ErrorHandler() { ErrorCode = Constants.ApplicationErrorCode, Message = Constants.ChecklistItemsPendingError });
                                }
                            }

                            ctrlApplication.UpdateApplicationQuotationStatus(id, (int)svcWorkflow.NewStatus.StatusId, action, data, editedFields, BO_ObjectAPI.quotation);
                            if(svcWorkflow.Action.ActionId == (int)BackOfficeEnum.Action.APPLICATION_SEND_QUOTE)
                            {
                                log.Info("[UpdateApplicatinCaseState] function called for application id : " + id);
                                ApplicationCaseUtil.UpdateApplicatinCaseState(id, ApplicationCaseObject.SENDFORAPPROVAL.ToString());
                            }
                        }
                        else if (category == BO_ObjectAPI.documents.ToString().ToLower())
                        {
                            if (svcWorkflow.TransitionFunction == "VerifyDocument")
                            {
                                try
                                {
                                    DocumentsDto doc = new DocumentsDto();
                                    doc = JsonConvert.DeserializeObject<DocumentsDto>(data);
                                    ctrlApp.VerifyDocument(id, doc);
                                }
                                catch (Exception ex)
                                {
                                    log.Error(string.Format("Error editing application document details: id {0}, action {1}, category {2}\nError: {3}", id, action, category, ex));
                                    throw;
                                }
                            }

                            else if (svcWorkflow.TransitionFunction == "RejectDocument")
                            {
                                try
                                {
                                    BOS_CustomerHistory doc = new BOS_CustomerHistory();
                                    doc = JsonConvert.DeserializeObject<BOS_CustomerHistory>(data);
                                    string RejectReason = doc.Comment;
                                    //string RejectReason = "We found errors in your document, please reupload again.";
                                    ctrlApp.RejectDocument(id, RejectReason);
                                }
                                catch (Exception ex)
                                {
                                    log.Error(string.Format("Error editing application employer details: id {0}, action {1}, category {2}\nError: {3}", id, action, category, ex));
                                    throw;
                                }
                            }

                            ctrlApplication.UpdateApplicationDocumentStatus(id, (int)svcWorkflow.NewStatus.StatusId, action, data, editedFields, BO_ObjectAPI.documents);
                        }
                        else if (category == BO_ObjectAPI.master.ToString().ToLower())
                        {
                            log.Info("category: master");
                            if (svcWorkflow.TransitionFunction.ToLower() == "new_application" || action.ToLower() == "new_application")
                            {
                                try
                                {
                                    log.Info("create new applicaiton");
                                    VMCustomerDetails customerDto = new VMCustomerDetails();
                                    customerDto = JsonConvert.DeserializeObject<VMCustomerDetails>(data);
                                    var result = ctrlApp.CreateApplication(id, customerDto);
                                    if (result.Status.ToLower() != Constants.Success.ToLower())
                                    {
                                        return Response<object>.CreateResponse(Constants.Failed, null, result.Error);
                                    }
                                    ctrlApp.UpdateApplicationEventHistory(id, action, data, editedFields, BO_ObjectAPI.master);
                                }
                                catch (Exception ex)
                                {
                                    log.Error(string.Format("Error creating new application: id {0}, action {1}, category {2}\nError: {3}", id, action, category, ex));
                                    throw;
                                }
                            }
                            else if (svcWorkflow.TransitionFunction.ToLower() == "fn_deductnucard")
                            {
                                try
                                {
                                    log.Info("execute fn_deductnucard");
                                    var result = ctrlApp.fn_DeductNuCard(id, documentId);
                                    var returnData = JsonConvert.SerializeObject(result);
                                    if (result.Status.ToLower() == "success")
                                    {
                                        ctrlApp.UpdateApplicationMasterStatus(id, (int)svcWorkflow.NewStatus.StatusId, action, data, editedFields, BO_ObjectAPI.master);
                                    }
                                    else
                                        return Response<object>.CreateResponse(Constants.Failed, null, new ErrorHandler() { ErrorCode = result.Error.ErrorCode, Message = result.Error.Message });
                                }
                                catch (Exception ex)
                                {
                                    log.Error(string.Format("Error executing fn_deductnucard: id {0}, action {1}, category {2}\nError: {3}", id, action, category, ex));
                                    throw;
                                }
                            }
                            else if (svcWorkflow.TransitionFunction.ToLower() == "fn_balancenucard")
                            {
                                try
                                {
                                    log.Info("execute fn_balancenucard");
                                    var result = ctrlApp.fn_BalanceNuCard(id, documentId);
                                    var returnData = JsonConvert.SerializeObject(result);
                                    if (result.Status.ToLower() == "success")
                                    {
                                        ctrlApp.UpdateApplicationMasterStatus(id, (int)svcWorkflow.NewStatus.StatusId, action, data, editedFields, BO_ObjectAPI.master);
                                    }
                                    else
                                        return Response<object>.CreateResponse(Constants.Failed, null, new ErrorHandler() { ErrorCode = result.Error.ErrorCode, Message = result.Error.Message });
                                }
                                catch (Exception ex)
                                {
                                    log.Error(string.Format("Error executing fn_balancenuard: id {0}, action {1}, category {2}\nError: {3}", id, action, category, ex));
                                    throw;
                                }
                            }
                            else
                            {
                                ctrlApp.UpdateApplicationMasterStatus(id, (int)svcWorkflow.NewStatus.StatusId, action, data, editedFields, BO_ObjectAPI.master);
                            }
                        }

                        var application = ctrlApplication.GetApplicationById(id);
                        log.Info(string.Format("Invoke application action success: id {0}, action {1}, category {2}", id, action, category));
                        return Response<object>.CreateResponse(Constants.Success, application.Data, null);
                    }
                }
                log.Info(string.Format("Transition function {0} not found. id {1}, action {2}, category {3}", svcWorkflow.TransitionFunction, id, action, category));
                return Response<object>.CreateResponse(Constants.Failed, null, new ErrorHandler() { ErrorCode = Constants.ActionsErrorCode, Message = Constants.ActionsInvalidMessage });
            }
            catch (Exception ex)
            {
                log.Error(string.Format("Error invoking application action: id {0}, action {1}, category {2}\nError: {3}", id, action, category, ex));
                throw;
            }
        }

        /// <summary>
        /// This method invokes the said action to be performed on the repayment object
        /// </summary>
        /// <param name="svcWorkflow">workflow service object that determines the next state of the object when action is performed</param>
        /// <param name="id">id of the object</param>
        /// <param name="action">action to be performed</param>
        /// <param name="data">paramaters, if any</param>
        private Response<object> InvokeRepaymentActions(BOS_WorkFlowService svcWorkflow, int id, string category, string action, string data, string api, string documentId)
        {
            try
            {
                log.Info(string.Format("Invoke repayment action: id {0}, action {1}, category {2}", id, action, category));
                var type = Type.GetType(typeof(RepaymentController).ToString());
                string editedFields = string.Empty;
                RepaymentController ctrlRepay = new RepaymentController();
                Response<string> result = null;
                log.Info("get parameter for method");
                MethodInfo methodInfo = null;
                methodInfo = typeof(RepaymentController).GetMethod(svcWorkflow.TransitionFunction);

                if (methodInfo != null)
                {
                    ParameterInfo[] parameters = methodInfo?.GetParameters();
                    if (parameters == null)
                    {
                        log.Info(string.Format("no parameters found. invoke method"));
                        methodInfo?.Invoke(type, null);
                        return Response<object>.CreateResponse(Constants.Failed, null, null);
                    }
                    else
                    {
                        log.Info(string.Format("parameters found"));
                        object classInstance = Activator.CreateInstance(type, null);
                        VMLoanRepayment repaymentmodel = new VMLoanRepayment();
                        repaymentmodel = JsonConvert.DeserializeObject<VMLoanRepayment>(data);
                        Response<String> doCancellationresponse = null;
                        if (svcWorkflow.TransitionFunction.ToLower() == "createrepayment")
                        {
                            try
                            {
                                log.Info("Create repayment");
                                var res = ctrlRepay.CreateRepayment(repaymentmodel);
                                var do_result = ctrlRepay.UpdateDebitOrder(repaymentmodel.AccountId, repaymentmodel.IsDebitOrderCancel);
                                if (res.Data != 0)
                                {
                                    ctrlRepay.UpdateRepaymentStatus(res.Data, (int)svcWorkflow.NewStatus.StatusId, action, data, editedFields);
                                    var repaymentDetails = ctrlRepay.GetRepayment((int)res.Data);
                                    if (repaymentDetails.Status.ToLower() == Constants.Success.ToLower())
                                    {
                                        ctrlRepay.UpdateAccountRepaypayemnt(repaymentmodel.AccountId, (int)svcWorkflow.NewStatus.StatusId, action, data, editedFields, repaymentDetails.Data.RepaymentId);
                                    }
                                    return Response<object>.CreateResponse(Constants.Success, repaymentDetails.Data, null);
                                }
                                else
                                {
                                    return Response<object>.CreateResponse(Constants.Failed, null, result.Error);
                                }
                            }
                            catch (Exception ex)
                            {
                                log.Error(string.Format("Error creating repaymet: id {0}, action {1}, category {2}\nError: {3}", id, action, category, ex));
                                throw;
                            }
                        }
                        if (svcWorkflow.TransitionFunction.ToLower() == "processrepayment")
                        {

                            //Cancel debit order for nucard
                            doCancellationresponse = ctrlRepay.CancelDebitOrderforNuCard(repaymentmodel.AccountId);
                            result = ctrlRepay.ProcessRepayment(id, (int)svcWorkflow.NewStatus.StatusId);
                            editedFields = result.Data;
                        }
                        else if (svcWorkflow.TransitionFunction.ToLower() == "reverserepayment")
                        {
                            result = ctrlRepay.ReverseRepayment(id, (int)svcWorkflow.NewStatus.StatusId, (int)svcWorkflow.Status.StatusId);
                            editedFields = result.Data;
                        }
                        else if (svcWorkflow.TransitionFunction.ToLower() == "refundrepayment")
                        {
                            try
                            {
                                log.Info("execute refund payment");
                                LoanRefundAmount _loanRefund = new LoanRefundAmount();
                                _loanRefund = JsonConvert.DeserializeObject<LoanRefundAmount>(data);
                                result = ctrlRepay.RefundRepayment(_loanRefund, (int)svcWorkflow.NewStatus.StatusId);
                                editedFields = result.Data;
                            }
                            catch (Exception ex)
                            {
                                log.Error(string.Format("Error in refund repayment: id {0}, action {1}, category {2}\nError: {3}", id, action, category, ex));
                                throw;
                            }
                        }

                        if (result.Status.ToLower() == Constants.Success.ToLower() && svcWorkflow.TransitionFunction.ToLower() != "createrepayment")
                        {
                            try
                            {
                                log.Info("update account repayment");
                                ctrlRepay.UpdateRepaymentStatus(id, (int)svcWorkflow.NewStatus.StatusId, action, data, editedFields);
                                var repaymentDetails = ctrlRepay.GetRepayment((int)id);
                                if (repaymentDetails.Status.ToLower() == Constants.Success.ToLower())
                                {
                                    ctrlRepay.UpdateAccountRepaypayemnt(repaymentDetails.Data.RepaymentDetails.AccountId, (int)svcWorkflow.NewStatus.StatusId, action, data, editedFields, id);
                                }
                                if (svcWorkflow.TransitionFunction.ToLower() == "processrepayment" && doCancellationresponse != null && doCancellationresponse.Status == Constants.Failed)
                                {
                                    return Response<object>.CreateResponse(Constants.Failed, repaymentDetails.Data, new ErrorHandler { ErrorCode = Constants.DebitOrderCancellationErrorCode, Message = Constants.DebitOrderCancellationErrorMessage.Replace("#Contract_Number", doCancellationresponse.Data) });
                                }
                                return Response<object>.CreateResponse(Constants.Success, repaymentDetails.Data, null);
                            }
                            catch (Exception ex)
                            {
                                log.Error(string.Format("Error updating account repayment: id {0}, action {1}, category {2}\nError: {3}", id, action, category, ex));
                                throw;
                            }
                        }

                        else if (svcWorkflow.TransitionFunction == "PrintRepaymentReciept")
                        {
                            try
                            {
                                log.Info("execute fn_printagree");
                                var byteArray = ctrlRepay.PrintRepaymentReciept(id);

                                if (byteArray == null)
                                    return Response<object>.CreateResponse(Constants.Failed, null, new ErrorHandler() { ErrorCode = Constants.ActionsErrorCode, Message = Constants.PrintQuoteExecutionErrorMessage });
                            }
                            catch (Exception ex)
                            {
                                log.Error(string.Format("Error executing fn_printquote: id {0}, action {1}, category {2}\nError: {3}", id, action, category, ex));
                                throw;
                            }
                        }
                        else
                        {
                            return Response<object>.CreateResponse(Constants.Failed, null, result.Error);
                        }
                    }
                }
                log.Info(string.Format("Transition function {0} not found. id {1}, action {2}, category {3}", svcWorkflow.TransitionFunction, id, action, category));
                return Response<object>.CreateResponse(Constants.Failed, null, new ErrorHandler() { ErrorCode = Constants.ActionsErrorCode, Message = Constants.ActionsInvalidMessage });
            }
            catch (Exception ex)
            {
                log.Error(string.Format("Error invoking repayment action: id {0}, action {1}, category {2}\nError: {3}", id, action, category, ex));
                throw;
            }
        }
    }
}

