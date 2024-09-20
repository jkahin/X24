using netForum.Integration.Webhooks;
using System;
using System.Linq;
using System.Net.Http;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using IncomingWebhooks.VolunteerModel;
using Avectra.netForum.Data;
using Avectra.netForum.Common.Extensions;
using Avectra.netForum.Common;
using Avectra.netForum.Components.EV;
using System.Collections.Generic;
using Avectra.netForum.Components.OE;
using Avectra.netForum.Components.AC;
using System.Data;
using static Avectra.netForum.Common.dbN;

namespace IncomingWebhooks
{
    public class VolunteerHandler
    {
        public HttpResponseMessage ProcessVolunteer(WebhookIntegration integrationSettings, HttpRequestMessage requestMessage)
        {
            ErrorClass errorClass = new ErrorClass();
            try
            {
                var requestContent = Volunteer.GetRequestContentAsString(requestMessage);

                VolunteerDto registration = null;

                errorClass = GetRequestData(requestMessage, requestContent, ref registration);

                if (errorClass != null)
                {
                    using (NfDbConnection oConn = DataUtils.GetConnection())
                    using (NfDbTransaction oTran = oConn.BeginTransaction())
                    {
                        errorClass = ValidEvent(oConn, oTran, registration.Event.Id);

                        if (!errorClass.HasError)
                            errorClass = ValidSessions(oConn, oTran, registration.Sessions);

                        if (!errorClass.HasError)
                        {
                            errorClass = GetIndividual(oConn, oTran, registration.Individual);

                            if (!errorClass.HasError)
                            {
                                errorClass = SetSessionId(oConn, oTran, registration);
                                errorClass = CancelPreviousRegistration(oConn, oTran, registration);
                                errorClass = SetRegisteredFlagForExistingSession(oConn, oTran, registration);  
                               
                                if (!errorClass.HasError && (registration.Sessions.Count(x => x.IsRegistered == false) > 0) )
                                    errorClass = AddVolunteer(oConn, oTran, registration);
                            }
                        }

                        if (errorClass.HasError)
                            oTran.Rollback();
                        else 
                            oTran.Commit(); 
                    }
                }

                HttpResponseMessage response = GetResponseMessage(requestMessage, errorClass);
                return response;
            }
            catch (Exception ex)
            {
                var response = new HttpResponseMessage()
                {
                    RequestMessage = requestMessage,
                    StatusCode = HttpStatusCode.InternalServerError,
                    Content = new StringContent("Error trying to create response in VolunteerHandler.ProcessVolunteer" +
                        $"{Environment.NewLine}{ex.Message}" +
                        $"{Environment.NewLine}{ex.StackTrace}")
                };
                return response;
            }
        }
             

        private ErrorClass SetSessionId(NfDbConnection oConn, NfDbTransaction oTran, VolunteerDto registration)
        {
            ErrorClass error = new ErrorClass();

            foreach (var ses in registration.Sessions)
            {
                string szSQL = @$"select ses_key  
                                 FROM ev_event (nolock)                                   
                                  join ev_session (nolock) on ses_evt_key = evt_key
                                 WHERE evt_key = '{registration.Event.Id}'                                    
                                     and ses_title = '{ses.Title}'
                                     and ses_delete_flag = 0 ";

                var sesKey = Convert.ToString(DataUtils.ExecuteScalar(szSQL));

                if (!string.IsNullOrEmpty(sesKey))
                    ses.Id = sesKey;
            }

            return error;
        }

        private static HttpResponseMessage GetResponseMessage(HttpRequestMessage requestMessage, ErrorClass errorClass)
        {
            var responseConent = new RegistrationResposeModel();
            if (errorClass.HasError)
            {
                responseConent.reponseMessage = errorClass.Message;
                errorClass.LogError();
            }
            else
                responseConent.reponseMessage = "Success!!";

            // Try to format the response as JSON
            var responseContentString = string.Empty;
            var responseContentType = string.Empty;

            responseContentString = JsonConvert.SerializeObject(responseConent);
            responseContentType = WebhookReceiverBase.MediaTypeApplicationJson;


            var response = new HttpResponseMessage()
            {
                RequestMessage = requestMessage,
                StatusCode = HttpStatusCode.OK,
                Content = responseContentType == WebhookReceiverBase.MediaTypeApplicationJson ?
                    new StringContent(responseContentString, System.Text.Encoding.UTF8, WebhookReceiverBase.MediaTypeApplicationJson) :
                    new StringContent(responseContentString, System.Text.Encoding.UTF8)
            };
            return response;
        }

        private ErrorClass SetRegisteredFlagForExistingSession(NfDbConnection oConn, NfDbTransaction oTran, VolunteerDto registration)
        {
            ErrorClass error = new ErrorClass();

            foreach (var ses in registration.Sessions)
            {
                string szSQL = @$"select ses_key  
                                 FROM ev_event (nolock) 
                                  join ev_registrant (nolock) on reg_evt_key = evt_key
                                  join ev_registrant_session (nolock) on rgs_reg_key = reg_key
                                  join ev_session (nolock) on ses_key = rgs_ses_key
                                 WHERE evt_key = '{registration.Event.Id}'   
                                     and reg_cst_key = '{registration.Individual.Id}'
                                     and ses_title = '{ses.Title}'
                                     and ses_delete_flag = 0
                                     and rgs_cancel_date is null";

                var sesKey = Convert.ToString(DataUtils.ExecuteScalar(szSQL));

                if(!string.IsNullOrEmpty(sesKey) )
                    ses.IsRegistered = true;
                
            }

            if (Config.LastError != null)
            {
                error.Number = -1;
                error.Message = "session check failure.";
            }

            return error;
        }

        private ErrorClass CancelPreviousRegistration(NfDbConnection oConn, NfDbTransaction oTran, VolunteerDto registration)
        {
            ErrorClass error = null;

            string concatenatedSessionTitleValues = string.Join("; ", registration.Sessions.Select(obj => obj.Title));

            string szSQL = $"exec dbo.client_demo_cancel_session_registration @cst_key = '{registration.Individual.Id}', @evt_key='{registration.Event.Id}', @execlude_sessions='{concatenatedSessionTitleValues}'";

            error = DataUtils.ExecuteSql(szSQL, oConn, oTran);
              
            return error;
        }

        private ErrorClass AddVolunteer(NfDbConnection oConn, NfDbTransaction oTran, VolunteerDto registration)
        {
            ErrorClass  errorClass = new ErrorClass();

            var oCOE = new OrderEntry();
            oCOE.SetValue("inv_cst_key", registration.Individual.Id);
            oCOE.SetValue("inv_cst_billing_key", registration.Individual.Id);
            oCOE.SetValue("inv_ind_cst_billing_key", registration.Individual.Id);
            oCOE.SetValue("inv_bat_key", Avectra.netForum.Components.AC.ac_utility.GetWebBatchKey());
            oCOE.LoadRelatedData(oConn, oTran);

            Registration oRegistration = (Registration)DataUtils.InstantiateFacadeObject("EventsRegistrant");            
            oRegistration.SetValue("reg_cst_key", registration.Individual.Id);
            oRegistration.SetValue("reg_evt_key", registration.Event.Id);           
            oRegistration.LoadRelatedData(oConn, oTran);


            string szSQL = $"exec dbo.ev_registrant_select_event_fees @cst_key = '{registration.Individual.Id}', @fee_evt_key='{registration.Event.Id}'";           
            DataSet oDs = DataUtils.GetDataSet( szSQL, oConn, oTran);

            if (oDs != null && oDs.Tables[0].Rows.Count > 0)
            {
                if (Convert.ToDecimal(oDs.Tables[0].Rows[0]["price"])  > 0 )
                {
                    errorClass.Number = -1;
                    errorClass.Message = $"This event is not free event: {registration.Event.Id}";
                }
                else
                {
                    InvoiceDetail oInvoiceDetail = CreateRegistrantInvoiceDetail(oConn, oTran, registration, oDs.Tables[0].Rows[0]);
                    oInvoiceDetail.bDoNotSaveWhenZeroPrice = true;
                    oRegistration.oInvoice.AddInvoiceDetailLine(oInvoiceDetail);

                    foreach (var session in registration.Sessions)
                    {
                        szSQL = $"exec [dbo].[ev_registrant_select_session_fees] @cst_key = '{registration.Individual.Id}', @ses_evt_key='{registration.Event.Id}'";
                        DataSet oDsSession = DataUtils.GetDataSet(szSQL, oConn, oTran);
                        if (oDsSession != null && oDsSession.Tables[0].Rows.Count > 0)
                        {
                            DataRow[] oRow = oDsSession.Tables[0].Select($"ses_key = '{session.Id}'");
                            if (oRow != null && session.IsRegistered == false)
                            {
                                InvoiceDetail oInvoiceDetailSession = CreateRegistrantInvoiceDetail(oConn, oTran, registration, oRow[0]);

                                oInvoiceDetailSession.bDoNotSaveWhenZeroPrice = true;
                                RegistrationDetail oRegistrationDetail = GetSessionRegistration(registration.Individual.Id, oRegistration.GetValue("reg_key"), session.Id, oInvoiceDetailSession.GetValue("ivd_prc_key"), oInvoiceDetailSession.GetValue("ivd_key"));
                                oRegistrationDetail.LoadRelatedData(oConn, oTran);
                                oRegistration.RegistrationDetails.Add(oRegistrationDetail);
                                oRegistration.oInvoice.AddInvoiceDetailLine(oInvoiceDetailSession);
                            }
                        }
                    }

                    if (oRegistration.RegistrationDetailCount > 0)
                    {
                        oCOE.Registrations.Add(oRegistration);
                        oCOE.SetValue("pin_check_amount", "0");
                        errorClass = oCOE.Insert(oConn, oTran);
                    }                    
                }
            }
            else
            {
                errorClass.Number = -1;
                errorClass.Message = $"No price avaialble for this event: {registration.Event.Id}";
            }
            return errorClass;
        }

        private static RegistrationDetail GetSessionRegistration(string cstKey, string regKey, string sesKey, string prcKey, string ivdKey)
        {
            RegistrationDetail oRegistrationDetail = (RegistrationDetail)DataUtils.InstantiateFacadeObject("EventsRegistrantSession");
            oRegistrationDetail.SetValue("rgs_ses_key", sesKey);
            oRegistrationDetail.SetValue("rgs_reg_key", regKey);
            oRegistrationDetail.SetValue("rgs_cst_key", cstKey);
            oRegistrationDetail.SetValue("rgs_prc_key", prcKey);
            oRegistrationDetail.SetValue("rgs_ivd_key", ivdKey);
            
            return oRegistrationDetail;
        }

        private InvoiceDetail CreateRegistrantInvoiceDetail(NfDbConnection oConn, NfDbTransaction oTran, VolunteerDto registration, DataRow oRow)
        {
            InvoiceDetail oInvoiceDetail = (InvoiceDetail)FacadeObjectFactory.CreateInvoiceDetail();
            oInvoiceDetail.SetValue("ivd_key", Guid.NewGuid().ToString());
            oInvoiceDetail.SetValue("ivd_cst_key", registration.Individual.Id);
            oInvoiceDetail.SetValue("ivd_prc_prd_key", oRow["prd_key"].ToString());
            oInvoiceDetail.SetValue("ivd_prc_key", oRow["prc_Key"].ToString());
            oInvoiceDetail.SetValue("inv_cst_billing_key", registration.Individual.Id);
            oInvoiceDetail.SetDefaults(oConn, oTran);
            oInvoiceDetail.LoadRelatedData(oConn, oTran);  
            oInvoiceDetail.SetValue("ivd_price", oRow["Price"].ToString());
            oInvoiceDetail.SetValue("ivd_prc_prd_ptp_key", oRow["ptp_key"].ToString());
            oInvoiceDetail.SetValue("ivd_cst_ship_key", registration.Individual.Id);
            oInvoiceDetail.SetValue("ivd_cxa_key", registration.Individual.CustomerAddressId);
            oInvoiceDetail.SetValue("ivd_qty", "1");

            return oInvoiceDetail;
        }

        private static ErrorClass GetRequestData(HttpRequestMessage requestMessage, string requestContent, ref VolunteerDto registration)
        {
            ErrorClass errorClass = new ErrorClass();   
            if (requestMessage.Content.Headers.ContentType.MediaType == WebhookReceiverBase.MediaTypeApplicationJson)
            {
                try
                {
                    registration = (VolunteerDto)Volunteer.jsonToObject<VolunteerDto>(requestContent);
                }
                catch (Exception ex)
                {
                    errorClass.Message  = $"Error reading JSON data:{Environment.NewLine}{ex.Message}{Environment.NewLine}{requestContent}";
                }
            }
            else
            {
                errorClass.Message = $"Error payload must be JSON format:{Environment.NewLine}{requestContent}";
            }

            return errorClass;  
        }

        private ErrorClass ValidEvent(NfDbConnection oConn, NfDbTransaction oTran, string eventKey)
        {
            ErrorClass errorClass = new ErrorClass();           
            string szSQL = string.Empty;

            if(!string.IsNullOrEmpty(eventKey))
            {
                szSQL = $"select top 1 evt_key from ev_event (nolock) where evt_key = '{eventKey}' and evt_delete_flag = 0";
                if (!DataUtils.DataExistsInSQL(szSQL, oConn, oTran))
                {
                    errorClass.Number = -1;
                    errorClass.Message = $"Event : {eventKey} is invalid";
                }               
            }          

            return errorClass;
        }

        private ErrorClass ValidSessions(NfDbConnection oConn, NfDbTransaction oTran, List<SessionRegistrationModel> sessionss)
        {
            ErrorClass errorClass = new ErrorClass();
          
            string szSQL = string.Empty;

            foreach(SessionRegistrationModel session in sessionss) 
            { 
                if ( !string.IsNullOrEmpty(session.Title))
                {
                    szSQL = $"select top 1 ses_key from ev_session (nolock) where ses_title = '{session.Title}' and ses_delete_flag = 0";
                    if (!DataUtils.DataExistsInSQL(szSQL, oConn, oTran))
                    {
                        errorClass.Number = -1;
                        errorClass.Message = $"Session title : {session.Title} is invalid";
                    }
                }

                if (errorClass.HasError)
                    break;
            }          

            return errorClass;
        }

        private ErrorClass GetIndividual(NfDbConnection oConn, NfDbTransaction oTran, RegistrantModel model)
        {
            ErrorClass oEr = new ErrorClass();  
            if (model != null)
            {
                string szSQL = $"select cst_key from co_customer (nolock) where cst_eml_address_dn = '{model.EmailAddress}' and cst_delete_flag = 0";
                model.Id = Convert.ToString(DataUtils.ExecuteScalar(szSQL, oConn, oTran));

                if(string.IsNullOrEmpty(model.Id))
                {
                    FacadeClass oIndividual = DataUtils.InstantiateFacadeObject("Individual");
                    oIndividual.SetValue("ind_first_name", model.FirstName);
                    oIndividual.SetValue("ind_last_name", model.LastName);
                    oIndividual.SetValue("eml_address", model.EmailAddress);
                    oIndividual.SetValue("adr_line1", "123 Main St");
                    oIndividual.SetValue("adr_city", "Mclean");
                    oIndividual.SetValue("adr_state", "VA");
                    oIndividual.SetValue("adr_country", "UNITED STATES");

                    oEr = oIndividual.Insert(oConn, oTran);
                    if (!oEr.HasError)
                    {
                        model.Id = oIndividual.CurrentKey;
                        szSQL = $"select cst_cxa_key from co_customer (nolock) where cst_key = '{model.Id}'";
                        model.CustomerAddressId = Convert.ToString(DataUtils.ExecuteScalar(szSQL));
                    }
                }
            }
            return oEr;
        }

    }
    




}
