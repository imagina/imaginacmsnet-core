﻿using Core.Events.Interfaces;
using Core.Exceptions;
using Core.Interfaces;
using Core.Logger;
using Core.Storage;
using Core.Storage.Interfaces;
using Core.Transformers;
using Ihelpers.Interfaces;
using jsreport.Binary;
using jsreport.Client;
using jsreport.Local;
using jsreport.Types;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Razor.Templating.Core;
using System.Data.SqlClient;
using System.Linq.Dynamic.Core;
using System.Runtime.InteropServices;
using TypeSupport.Extensions;
using Ihelpers.Helpers;
using Ihelpers.Helpers.Interfaces;
using Idata.Data;
using Microsoft.EntityFrameworkCore;
using Idata.Entities.Core;
using Idata.Entities.Isite;
using System.Reflection;

namespace Core.Repositories
{
    public class RepositoryBase<TEntity> : IRepositoryBase<TEntity> where TEntity : EntityBase
    {
        //DataContext of DB
        protected IdataContext _dataContext; //Repository<UnaEntidad>

        //Instance of type <TEntity>
        protected readonly dynamic? _entity = Activator.CreateInstance(typeof(TEntity));

        //Event Handling
        public IEventHandlerBase<TEntity>? _eventHandler;

        //User that performs requests
        protected dynamic? _contextUser;

        //DbSet of TEntity wich means _dataContext.TEntity
        protected DbSet<TEntity> _dbSet { get; set; }

        //Decoupled cache can be changed without any broken line
        public ICacheBase _cache;

        //This service is used to export any TEntity class to CSV.
        public IClassHelper<TEntity> _classHelper;

        //Interface that provides storage functions
        public IStorageBase _storageBase;

        //Only for loggin exceptions, avoid self reference loop error with urlRequestBase.CurrentContextUser;
        private JsonSerializerSettings jsonSerializerSettings = new JsonSerializerSettings
        {
            ReferenceLoopHandling = ReferenceLoopHandling.Ignore
        };
        public RepositoryBase()
        {
            _dataContext = new IdataContext();
            try
            {

            _dbSet =_dataContext.Set<TEntity>();
            }
            catch 
            {
            }
        }
        public virtual async Task Initialize(dynamic wichContext)
        {
           _dataContext = wichContext;

            _dbSet =_dataContext.Set<TEntity>();



        }
        public virtual async Task Initialize(dynamic wichContext, dynamic wichUser)
        {
           _dataContext = wichContext;

            _dbSet =_dataContext.Set<TEntity>();

            _contextUser = wichUser;


        }

        /// <summary>
        /// Get a list of entities, taking in count pagination and filters sent inside requestBase
        /// </summary>
        /// <param name="requestBase">Standard request that holds filters and pagination configuration for this method</param>
        /// <returns>Returns a PaginatedList<typeparamref name="TEntity"/> of the entity</returns>
        public virtual async Task<List<TEntity?>> GetItemsBy(UrlRequestBase? requestBase)
        {
            PaginatedList<TEntity>? resultList = null;

            //set the current context user 
            requestBase.setCurrentContextUser(_contextUser);

            try
            {
                IQueryable<TEntity> query = _dbSet;

                // Custom filters from the Repository child
                this.CustomFilters(ref query, ref requestBase);

                //Verify the filters that are present in requestBase
                // adding dynamic filters 
                query = requestBase.SetDynamicFilters(query, _entity);

                //Get includes and apply them
                query = requestBase.GetIncludes(query, _entity);

                //Use of PaginatedList to get the items and the meta of them
                resultList = await PaginatedList<TEntity>.CreateAsync(query, requestBase.page, requestBase.take);

                //Initialize each model
                resultList.ForEach(p => p.Initialize());

                //TODO: comentar por qué se hizo esto

                //If includeParent filter is sent by front (same table parent of record)
                if (requestBase.includeParent)
                {
                    foreach (var result in resultList)
                    {
                        //To be able to cast .parent property in runtime
                        dynamic resultInDynamic = result;
                        //Convertion needed because DynamicLINQ is not compatible with dynamics parameters
                        long? parentID = Convert.ToInt64(resultInDynamic.parent_id);
                        //get the parent of this entity
                        resultInDynamic.parent = await _dbSet.Where($"obj => obj.id == @0", parentID).FirstOrDefaultAsync();


                    }
                }


                Task.Factory.StartNew(() => LogAction($"has listed: {typeof(TEntity).Name}", logType: LogType.Information, requestBase: requestBase));




            }
            catch (Exception ex)
            {

                ExceptionBase.HandleException(ex, $"Error obtaining list of {typeof(TEntity).Name}", $"ExceptionMessage = {ex.Message}  trace received: " + JsonConvert.SerializeObject(requestBase, jsonSerializerSettings).Trim().Replace("\"", "'"));

            }

            return resultList;
        } 

        /// <summary>
        /// Create a report for any entity that inherits from EntityBase, taking in count pagination and filters from requestBase  and configuration sent inside bodyRequestBase.exportParams property
        /// </summary>
        /// <param name="requestBase">Standard request that holds pagination configuration for this method</param>
        /// <param name="bodyRequestBase">Standard body request that holds filter configuration, fileformat, filename inside exportparam property</param>
        /// <returns>This method is meant to be ran inside a hangfire job, not directly</returns>
        public virtual async Task CreateExport(UrlRequestBase? requestBase, BodyRequestBase? bodyRequestBase)
        {
            List<TEntity?>? resultList = null;


            _classHelper = new ClassHelper<TEntity>();

            _storageBase = new AzureStorageBase();

            try
            {
                //avoid inconsistent data results for each row 
                requestBase.selectDefaultIncludes = false;

                //setting filters
                requestBase.filter = bodyRequestBase.filter != null ? bodyRequestBase.filter.ToString() : null;
                requestBase.doNotCheckPermissions();
                await requestBase.Parse();

     
               resultList = await GetItemsBy(requestBase);




                //if procedure not exists continue with standard way

                string[]? fields = JObjectHelper.GetJObjectValue<string[]?>(bodyRequestBase.exportParams, "fields");

                string[]? headings = JObjectHelper.GetJObjectValue<string[]?>(bodyRequestBase.exportParams, "headings");

                string? fileFormat = JObjectHelper.GetJObjectValue<string>(bodyRequestBase.exportParams, "fileFormat");

                if (string.IsNullOrEmpty(fileFormat) || fileFormat == "csv")
                {

                    //Get the list of items from repo

                    string procedureName = string.IsNullOrEmpty(requestBase.procedureName) ? $"SPExportCSV{typeof(TEntity).Name}" : requestBase.procedureName;

                    //Detect if proceed with store procedure
                    bool procedureExists = await Ihelpers.Helpers.EntityFrameworkCoreHelper.StoredProcedureExists((DatabaseFacade)_dataContext.Database, $"{procedureName}");

                    //Get the filename from exportparams
                    string? fileName = JObjectHelper.GetJObjectValue(bodyRequestBase.exportParams, "fileName").ToString() + $"{requestBase.currentContextUser.id}";

                    LogAction($"Begin with csv report creation for Entity: {typeof(TEntity).Name}, FileName: {fileName}", logType: LogType.Information, requestBase: requestBase);

                    fileName = (string.IsNullOrEmpty(fileFormat) || fileFormat == "csv") ? fileName += ".csv" : fileName += $".{fileFormat}";




                    if (procedureExists)
                    {


                        LogAction($"Stored Procedure {procedureName} encountered on current context: {typeof(TEntity).Name}", logType: LogType.Information, requestBase: requestBase);
                        //create the @id_list 
                        string id_list = string.Join(';', resultList.Select(c => c.id));

                        //Create the SP parameter with the given list
                        SqlParameter param = new SqlParameter("id_list", id_list);

                        //The sql is the name of the procedure
                        string sql = $"{procedureName}";

                        var csvDict = await EntityFrameworkCoreHelper.FromSqlQueryToDictionary((DatabaseFacade)_dataContext.Database, sql, System.Data.CommandType.StoredProcedure, param).SingleAsync();

                        if (csvDict != null)
                        {
                            string csvText = string.Join(Environment.NewLine, Ihelpers.Helpers.TypeHelper.ToCsv(csvDict, '\t'));

                            Stream csvStream = _classHelper.GenerateStreamFromString(csvText);

                            fileName += ".csv";

                            //Upload the file to storage handler
                            await _storageBase.CreateFile(fileName, csvStream);
                        }
                        else
                        {
                            //throw exception

                        }

                    }
                    else
                    {

                        LogAction($"Stored Procedure {procedureName} doesn't exist on current DB Context", logType: LogType.Information, requestBase: requestBase);

                        Stream csvStream = _classHelper.toCsvFile(resultList, fields.ToList(), headings.ToList());


                        //Upload the file to storage handler
                        await _storageBase.CreateFile(fileName, csvStream);


                    }

                }
                else if(fileFormat == "pdf")
                {

                    //CONVERT RESULTS TO FIRST LVL 
                    var transformedItems = await TransformerBase.TransformCollection(resultList);

                    var pathFields = resultList.GetPropertiesPath();
                    
                    //Add all non relation properties to pathfields

                    string result = await TransformerBase.GetCSVReport<TEntity>(transformedItems, pathFields);

                    #region PDF
                    //PDF par
                    var html = await RazorTemplateEngine.RenderAsync("~/CSVReport/CSVReport.cshtml", result);


					int ammountItems = resultList.Count();

                    resultList.Clear();



                    string? url = Ihelpers.Helpers.ConfigurationHelper.GetConfig<string>("JsReportingServices:Url");

                    string? userName = Ihelpers.Helpers.ConfigurationHelper.GetConfig<string>("JsReportingServices:UserName");

                    string? password = Ihelpers.Helpers.ConfigurationHelper.GetConfig<string>("JsReportingServices:Password");


                    var rs = new ReportingService(url, userName, password);


                    var generatedPdf = await rs.RenderAsync(new RenderRequest
                    {
                        Template = new Template
                        {
                            Recipe = Recipe.ChromePdf,

                            Engine = Engine.None,
                            Content = html,
                            Chrome = new Chrome
                            {
                                MarginTop = "10",
                                MarginBottom = "10",
                                MarginLeft = "50",
                                MarginRight = "50",
                                Landscape = true,
                            }
                           
                        }
                    });

					#endregion



					//fileName = fileName += $".{fileFormat}";

					//Upload the file to storage handler
					await _storageBase.CreateFile("testing.pdf", generatedPdf.Content);
                }
                else
                {
					//CONVERT RESULTS TO FIRST LVL 
					var transformedItems = await TransformerBase.TransformCollection(resultList);

					var pathFields = resultList.GetPropertiesPath();

					//Add all non relation properties to pathfields

					string result = await TransformerBase.GetCSVReport<TEntity>(transformedItems, pathFields);

					#region EXCEL
					//EXCEL part
					var html = await RazorTemplateEngine.RenderAsync("~/CSVReport/CSVReport.cshtml", result);
                    //Extract only the table HTML
                    string table = html.Split("<body>").Last().Split("</body>").First().Trim();

					int ammountItems = resultList.Count();

					resultList.Clear();



					string? url = Ihelpers.Helpers.ConfigurationHelper.GetConfig<string>("JsReportingServices:Url");

					string? userName = Ihelpers.Helpers.ConfigurationHelper.GetConfig<string>("JsReportingServices:UserName");

					string? password = Ihelpers.Helpers.ConfigurationHelper.GetConfig<string>("JsReportingServices:Password");


                    var rs = new LocalReporting()
								.KillRunningJsReportProcesses()
								.UseBinary(RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? JsReportBinary.GetBinary() : jsreport.Binary.Linux.JsReportBinary.GetBinary())
								.Configure(cfg => cfg.AllowedLocalFilesAccess().FileSystemStore().BaseUrlAsWorkingDirectory())
								.AsUtility()
								.Create();//new ReportingService(url, userName, password);


					var generatedPdf = await rs.RenderAsync(new RenderRequest
                    {
                        Template = new Template
                        {
                            Recipe = Recipe.HtmlToXlsx,

                            Engine = Engine.JsRender,
                            Content = table,
                            
                        }, Options = new RenderOptions
                        {
                          Timeout = 10000000
                        }
                    }); 

					#endregion

					//Upload the file to storage handler
					await _storageBase.CreateFile("testing.xlsx", generatedPdf.Content);
				}






				//Insert the creation log for  Entity

				Task.Factory.StartNew(() => LogAction($"has created export for: {typeof(TEntity).Name}", logType: LogType.Information, requestBase: requestBase));



            }
            catch (Exception ex)
            {
                ExceptionBase.HandleException(ex, $"Error obtaining list of {typeof(TEntity).Name}", $"ExceptionMessage = {ex.Message}   trace received: " + JsonConvert.SerializeObject(requestBase, jsonSerializerSettings).Trim().Replace("\"", "'"));
            }

        }

        /// <summary>
        /// Get an entity, taking in count filters sent inside requestBase
        /// </summary>
        /// <param name="requestBase">Standard request that holds filters and pagination configuration for this method</param>
        /// <returns>Returns the item or null if item doesn't exists</returns>
        public virtual async Task<TEntity?> GetItem(UrlRequestBase? requestBase)
        {
            TEntity? model = null; //object to be returned
            //set the current context user 
            requestBase.setCurrentContextUser(_contextUser);

            try
            {

                //Try get the search filter
                string field = requestBase.GetFilter("field");

                //Create base query based on criteria and field
                var query = _dbSet.Where($"obj => obj.{field} == @0", requestBase.criteria);

                // Custom filters from the Repository child
                this.CustomFilters(ref query, ref requestBase);

                // adding dynamic filters 
                query = requestBase.SetDynamicFilters(query, _entity);


                //query.GetIncludes(requestBase);
                query = requestBase.GetIncludes(query, _entity);

                //get the model with given criteria
                model = await query.FirstOrDefaultAsync();

                //if model is null (not found) throw exception
                if (model == null) throw new ExceptionBase($"{typeof(TEntity).Name} with {field} {requestBase.criteria} not found ", 404);

                ////Concatenate permissions and construct allPermissions of item
                model.Initialize();

                //Insert the creation log for  Entity
                Task.Factory.StartNew(() => LogAction($"has seen: {typeof(TEntity).Name}, id: {model.id}", logType: LogType.Information, requestBase: requestBase));


                //if the model is valid return the item
                return model;

            }
            catch (Exception ex)
            {
                ExceptionBase.HandleException(ex, $"Error obtaining {typeof(TEntity).Name}", $"ExceptionMessage = {ex.Message}   trace received: " + JsonConvert.SerializeObject(requestBase, jsonSerializerSettings).Trim().Replace("\"", "'"));

            }
            return model;
        }

        /// <summary>
        /// Creates an entity, taking in count relationships if any
        /// </summary>
        /// <param name="requestBase">Standard request that holds filters for this method</param>
        /// /// <param name="bodyRequestBase">Standard request that holds the entity to be created inside attributes property</param>
        /// <returns>Returns the item or null if item doesn't exists</returns>
        public virtual async Task<TEntity?> Create(UrlRequestBase? requestBase, BodyRequestBase? bodyRequestBase)
        {
            //TODO handle the two types of create without any override 
            //Transaction must be declared here because throwing exceptions need to rollback the transaction
            Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? transaction = null;
            Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? transactionRevision = null;

            TEntity? common = null;

            //set the current context user 
            requestBase.setCurrentContextUser(_contextUser);
            try
            {
                //Get the request timezone
                string? requestTimezone = requestBase.getRequestTimezone();

                //Deserialize object that will be created into a model
                common = await TransformerBase.ToClass<TEntity>(bodyRequestBase._attributes, userTimezone: _contextUser?.timezone?? requestTimezone);


                //model validations
                if (common == null) // || string.IsNullOrEmpty(common.email) || string.IsNullOrEmpty(common.password))
                {
                    throw new ExceptionBase($"Error parsing object {typeof(TEntity).Name}", 404);
                }
                 
                //Notify that entity of type TEntity is creating before any database operation
                if (_eventHandler != null) (_eventHandler.getEventBase()).FireEntityIsCreating(common);

                //get context Token
                string? contextToken = requestBase.getCurrentContextToken();

                //if token is not null get the id of creator from token, if null log the warning
                if (contextToken != null)
                {
                    string? userIdstr = await JWTHelper.getJWTTokenClaimAsync(contextToken, "UserId");

                    if (userIdstr != null) common.created_by = long.Parse(userIdstr);

                }
                else
                {
                    Logger.CoreLogger.LogMessage($"Token for create {typeof(TEntity).Name} object is empty, identity fields will be null",
                        null,
                        Ihelpers.Helpers.LogType.Warning);
                }


                //Begin the transaction
                transaction = await _dataContext.Database.BeginTransactionAsync();

                //save the model in database and commit transaction
                await _dbSet.AddAsync(common);

                await _dataContext.SaveChangesAsync(CancellationToken.None);

                await transaction.CommitAsync();

                if (common.dynamic_parameters.Count > 0)
                {
                    dynamic? relations = null;

                    common.dynamic_parameters.TryGetValue("relations", out relations);


                    if (relations != null)
                    {

                        IQueryable<TEntity> query = _dbSet.Where(obj => obj.id == common.id);

                        if (relations != null)
                        {
                            foreach (dynamic relation in relations)
                            {
                                string relationString = relation.Key.ToString();

                                query = query.Include(relationString);
                            }
                        }

                        //Existen relaciones
                        var model = await query.SingleOrDefaultAsync();

                        await SyncRelations(model, relations, _dataContext);


                    }


                }

                //call the logAction method to log the message
                Task.Factory.StartNew(() => LogAction($"has created: {typeof(TEntity).Name}, id: {common.id} ", logType: LogType.Information, requestBase: requestBase));


                if (await ClassHelper.GetValObjDy(_entity, "is_revisionable"))
                {
                    Type tentity = typeof(TEntity);
                    Revision NewRevision = new Revision();
                    NewRevision.old_value = null;
                    NewRevision.new_value = JsonConvert.SerializeObject(common);
                    NewRevision.revisionable_type = tentity.Namespace + "." + tentity.Name;
                    NewRevision.revisionable_id = common.id;
                    NewRevision.key = "Create Data";

                    //save the model in database
                    await _dataContext.Revisions.AddAsync(NewRevision);

                    await _dataContext.SaveChangesAsync(CancellationToken.None);

                }

                //Notify that entity of type TEntity was created after all database operations
                if (_eventHandler != null) (_eventHandler.getEventBase()).FireEntityWasCreated(common);

                return common;

            }
            catch (Exception ex)
            {
                ExceptionBase.HandleException(ex, $"Error creating {typeof(TEntity).Name} ", $"ExceptionMessage = {ex.Message}   trace received: " + JsonConvert.SerializeObject(bodyRequestBase._attributes, jsonSerializerSettings).Trim().Replace("\"", "'"), transaction);
            }
            return common;
        }

        /// <summary>
        /// Updates an entity, taking in count relationships if any
        /// </summary>
        /// <param name="requestBase">Standard request that holds filters for this method</param>
        /// /// <param name="bodyRequestBase">Standard request that holds the entity to be update inside attributes property, aswell as its relations</param>
        /// <returns>Returns the item or null if item doesn't exists</returns>
        public virtual async Task<TEntity?> UpdateBy(UrlRequestBase? requestBase, BodyRequestBase? bodyRequestBase)
        {
            //TODO handle the two types of update without any override 
            //TODO parametrized general entity relation sync on update that requires to

            //Transaction must be declared here because throwing exceptions need to rollback the transaction
            Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? transaction = null;

            dynamic? common = null;

            //set the current context user 
            requestBase.setCurrentContextUser(_contextUser);
            try
            {
                //Disparar el evento
                //_eventBase<TEntity>.FireUpdate();
                //Convert the json to the required class without adding relations (EF way to work)

                string? requestTimezone = requestBase.getRequestTimezone();

                //Deserialize object that will be created into a model
                common = await TransformerBase.ToClass<TEntity>(bodyRequestBase._attributes, userTimezone: _contextUser?.timezone ?? requestTimezone);

                //Extract sent 

                

                //Begin the transaction
                transaction = await _dataContext.Database.BeginTransactionAsync();

                //model validations
                if (common == null)
                {
                    throw new ExceptionBase($"Error parsing object {typeof(TEntity).Name}", 404);
                }

                

                //Get filters
                string field = requestBase.GetFilter("field");

                if (requestBase.criteria == null)
                    requestBase.criteria = common.id.ToString();
                else
                    try { common.id = long.Parse(requestBase.criteria); } catch { }


                var modelToUpdateQuery = _dbSet.Where($"obj => obj.{field} == @0", requestBase.criteria);

                
                if (common.dynamic_parameters.Count > 0)
                {
                    dynamic? relations = null;

                    common.dynamic_parameters.TryGetValue("relations", out relations);

                    if (relations != null)
                    {
                        foreach (dynamic relation in relations)
                        {
                            string relationString = relation.Key.ToString();
                            modelToUpdateQuery = modelToUpdateQuery.Include(relationString);
                        }


                    }


                }



                //get the model that will be updated
                var modelToUpdate = await modelToUpdateQuery.SingleOrDefaultAsync();

                common.id = common.id ?? modelToUpdate.id;

                //if the model is null then throw exception and rollback transaction if not then update
                if (modelToUpdate != null)
                {
                    //User Auth Me y evaluar allPermissions con el permiso iprofile.users.edit-permissions
                    //var user = Cache("authUser");
                    ////si existe y si es false
                    //if (existe && !user.allPermissions["iprofile.users.edit-permissions"]){
                    //    common.permissions = "";
                    //}

                    if (await ClassHelper.GetValObjDy(_entity, "is_revisionable"))
                    {
                        Type tentity = typeof(TEntity);
                        Revision NewRevision = new Revision();
                        NewRevision.old_value = JObjectHelper.SerializeObjectSafe(modelToUpdate);
                        NewRevision.new_value = JObjectHelper.SerializeObjectSafe(common);
                        NewRevision.revisionable_type = tentity.Namespace + "." + tentity.Name;
                        NewRevision.revisionable_id = common.id;
                        NewRevision.key = "Update Data";

                        //save the model in database
                        await _dataContext.Revisions.AddAsync(NewRevision);
                        await _dataContext.SaveChangesAsync(CancellationToken.None);
                    }

                    //Set the values that were sent in front to the db object

                    await UpdateProperties(common, modelToUpdate, $"{JObject.Parse(bodyRequestBase._attributes)}");


                    //catching custom before update 
                    this.BeforeUpdate(ref modelToUpdate, ref requestBase, ref bodyRequestBase);


                    //Notify that entity of type TEntity is updating before any database operation
                    if (_eventHandler != null) (_eventHandler.getEventBase()).FireEntityIsUpdating(modelToUpdate);

                    //get context Token
                    string? contextToken = requestBase.getCurrentContextToken();

                    //if token is not null get the id of creator from token, if null log the warning
                    if (contextToken != null)
                    {
                        string? userIdstr = await JWTHelper.getJWTTokenClaimAsync(contextToken, "UserId");

                        if (userIdstr != null) modelToUpdate.updated_by = long.Parse(userIdstr);

                    }
                    else
                    {
                        Logger.CoreLogger.LogMessage($"Token for update {typeof(TEntity).Name} object is empty, identity fields will be null",
                            null,
                           Ihelpers.Helpers.LogType.Warning);
                    }


                    try
                    {
                       _dataContext.Entry(modelToUpdate).Property("password").IsModified = false;

                    }
                    catch
                    {
                    }


                    await _dataContext.SaveChangesAsync(CancellationToken.None);
                    await transaction.CommitAsync();




                    if (common.dynamic_parameters.Count > 0)
                    {

                        dynamic? relations = null;

                        common.dynamic_parameters.TryGetValue("relations", out relations);

                        if (relations != null)
                        {
                            await SyncRelations(modelToUpdate, relations,_dataContext);

                        }


                    }


                    //Insert the creation log for  Entity
                    Task.Factory.StartNew(() => LogAction($"has updated: {typeof(TEntity).Name}, id: {common.id}", logType: LogType.Information, requestBase: requestBase));

                    

                    //Notify that entity of type TEntity was updated after all database operations
                    if (_eventHandler != null) (_eventHandler.getEventBase()).FireEntityWasUpdated(modelToUpdate);

                }
                else
                {
                    throw new ExceptionBase($"Item with {field} {requestBase.criteria} not found", 404);
                }
            }
            catch (Exception ex)
            {

                ExceptionBase.HandleException(ex, $"Error updating {typeof(TEntity).Name}", $"ExceptionMessage = {ex.Message}   trace received: " + JsonConvert.SerializeObject(bodyRequestBase._attributes, jsonSerializerSettings).Trim().Replace("\"","'"), transaction);

            }

            return common;
        }

        /// <summary>
        /// Deletes an entity, taking in count filters sent inside requestBase
        /// </summary>
        /// <param name="requestBase">Standard request that holds filters and pagination configuration for this method</param>
        /// <returns>Returns the item or null if item doesn't exists</returns>
        public virtual async Task<TEntity?> DeleteBy(UrlRequestBase? requestBase, dynamic? toReplaceModelToRemove= null)
        {
            //Transaction must be declared here because throwing exceptions need to rollback the transaction
            Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? transaction = null;

            //set the current context user 
            requestBase.setCurrentContextUser(_contextUser);
            try
            {
                //Try get the search filter
                string field = requestBase.GetFilter("field");

                //Begin the transaction
                transaction = await _dataContext.Database.BeginTransactionAsync();

                //get the model that will be removed
                var modelToRemoveQuery = _dbSet.Where($"obj => obj.{field} == @0", requestBase.criteria);

                //TODO inyect the include parameters as given in front
                modelToRemoveQuery = requestBase.GetIncludes(modelToRemoveQuery, _entity);


                var modelToRemove = await modelToRemoveQuery.FirstOrDefaultAsync();

                if (toReplaceModelToRemove != null)
                {
                    modelToRemove = toReplaceModelToRemove;
                }

                //if the model is null then throw exception and rollback transaction if not then delete
                if (modelToRemove != null)
                {

                    modelToRemove.Initialize();

                    //Notify that entity of type TEntity is deleting before delete database operation
                    if (_eventHandler != null) (_eventHandler.getEventBase()).FireEntityIsDeleting(modelToRemove);


                    //delete or soft_delete the model from DB, save changes and commit transaction based on force_delete

                    
                    if (modelToRemove.force_delete.Value)
                    {

                        _dbSet.Remove(modelToRemove);
                    }
                    else
                    {
                        modelToRemove.deleted_at = DateTime.UtcNow;

                        modelToRemove.deleted_by = requestBase.currentContextUser?.id ?? null;
                    }

                   

                    await _dataContext.SaveChangesAsync(CancellationToken.None);

                    await transaction.CommitAsync();

                    //Insert the deletion log for  Entity
                    Task.Factory.StartNew(() => LogAction($"has {(modelToRemove.force_delete.Value ? "deleted" : "soft deleted")}: {typeof(TEntity).Name}, id: {modelToRemove.id}", logType: LogType.Information, requestBase: requestBase));


                    //Notify that entity of type TEntity was deleted after delete database operation
                    if (_eventHandler != null) (_eventHandler.getEventBase()).FireEntityWasDeleted(modelToRemove);
                }
                else
                {
                    throw new ExceptionBase($"Item with field: {field} = {requestBase.criteria} not found", 404);
                }
            }
            catch (Exception ex)
            {

                ExceptionBase.HandleException(ex, $"Error deleting {typeof(TEntity).Name}", $"ExceptionMessage = {ex.Message}   trace received: " + JsonConvert.SerializeObject(requestBase, jsonSerializerSettings).Trim().Replace("\"", "'"), transaction);
            }

            return null;
        }


        /// <summary>
        /// Restores a previous deleted entity, taking in count filters sent inside requestBase
        /// </summary>
        /// <param name="requestBase">Standard request that holds filters and pagination configuration for this method</param>
        /// <returns>Returns the restored item or error if any error occurs</returns>
        public virtual async Task<TEntity?> RestoreBy(UrlRequestBase? requestBase)
        {
            //Transaction must be declared here because throwing exceptions need to rollback the transaction
            Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? transaction = null;

            //set the current context user 
            requestBase.setCurrentContextUser(_contextUser);
            try
            {
                //Try get the search filter
                string field = requestBase.GetFilter("field");

                //Begin the transaction
                transaction = await _dataContext.Database.BeginTransactionAsync();

                //get the model that will be restored
                var query = _dbSet.Where($"obj => obj.{field} == @0", requestBase.criteria);

                //inyect the include parameters as given in front
                query = requestBase.GetIncludes(query, _entity);


                var dbModel = await query.FirstOrDefaultAsync();



                //if the model is null then throw exception and rollback transaction if not then restore
                if (dbModel != null)
                {

                    //Notify that entity of type TEntity is restoring before restore database operation
                    if (_eventHandler != null) (_eventHandler.getEventBase()).FireEntityIsDeleting(dbModel);


                    //restore the model in db, save changes and commit transaction

                    dbModel.deleted_at = null;

                    dbModel.deleted_by = null;

                    dbModel.restored_at = DateTime.UtcNow;

                    dbModel.restored_by = requestBase.currentContextUser?.id ?? null;

                    await _dataContext.SaveChangesAsync(CancellationToken.None);

                    await transaction.CommitAsync();

                    //Insert the restore log for  Entity
                    Task.Factory.StartNew(() => LogAction($"has restored: {typeof(TEntity).Name}, id: {dbModel.id}", logType: LogType.Information, requestBase: requestBase));


                    //Notify that entity of type TEntity was restore after delete database operation
                    if (_eventHandler != null) (_eventHandler.getEventBase()).FireEntityWasDeleted(dbModel);
                }
                else
                {
                    throw new ExceptionBase($"Item with field: {field} = {requestBase.criteria} not found", 404);
                }
            }
            catch (Exception ex)
            {

                ExceptionBase.HandleException(ex, $"Error deleting {typeof(TEntity).Name}", $"ExceptionMessage = {ex.Message}   trace received: " + JsonConvert.SerializeObject(requestBase, jsonSerializerSettings).Trim().Replace("\"", "'"), transaction);
            }

            return null;
        }
        /// <summary>
        /// This method is ment to be overrided inside the child class, meant to sync all relations of entity.
        /// </summary>
        /// <param name="input">Object that is created or updated.</param>
        /// <param name="relations">Relations of the object that is created or updated.</param>
        /// <param name="dataContext">The DatContext that stores the object</param>
        /// <returns></returns>
        public virtual async Task SyncRelations(object? input, dynamic relations, dynamic _dataContext)
        {
            return;
        }

        /// <summary>
        /// This method is meant to be overrided for the child class, meant to apply custom filters to the query.
        /// </summary>
        /// <param name="query">Raw IQueryable[TEntity] </param>
        /// <param name="requestBase">Standard request that holds filters for this method, to applied</param>
        public virtual void CustomFilters(ref IQueryable<TEntity> query, ref UrlRequestBase? requestBase)
        {
            return;
        }

        /// <summary>
        /// This version is meant to fire before entity update.
        /// </summary>
        /// <param name="common">object that is beign updated</param>
        /// <param name="requestBase">Standard request that holds filters for this method, to applied</param>
        /// <param name="bodyRequestBase">Standard request that holds the entity to be update inside attributes property, aswell as its relations</param>
        public virtual void BeforeUpdate(ref TEntity? common, ref UrlRequestBase? requestBase, ref BodyRequestBase? bodyRequestBase)
        {
            return;

        }

        /// <summary>
        /// Log action about CRUD operations or custom operations.
        /// </summary>
        /// <param name="message">The message of the operation (user Email found will prefix the message)</param>
        /// <param name="requestBase">necesary param to extract current user of</param>
        /// <param name="logType"> default is Information</param>
        /// <returns></returns>
        public virtual async Task LogAction(string message, UrlRequestBase? requestBase, LogType logType = LogType.Information)
        {
            //If this was set before calling the repository, log the message that was set.
            if (requestBase.actionMessage != null)
            {
                CoreLogger.LogMessage(requestBase.actionMessage, logType: logType);
                return;
            }
            //try get the actual user from urlRequestbase
            dynamic? user = (requestBase.currentContextUser != null) ? requestBase.currentContextUser : null;
            //try get the email of the actual user
            string userEmail = (user != null) ? (string)user.email : "Anonymous";
            //try get the userId of the actual user
            long? userId = (user != null) ? (long?)user.id : null;
            //prefix the email to the message
            message = $"{userEmail} " + message;
            //log the action with userId
            CoreLogger.LogMessage(message, logType: logType, userId: userId);

        }

        /// <summary>
        /// Dynamic update without set all properties
        /// </summary>
        /// <param name="requestBase"></param>
        /// <param name="bodyRequestBase"></param>
        /// <returns></returns>
        /// <exception cref="ExceptionBase"></exception>
        public virtual async Task<TEntity?> UpdateOrdering(UrlRequestBase? requestBase, BodyRequestBase? bodyRequestBase)
        {
            //TODO handle the two types of update without any override 
            //TODO parametrized general entity relation sync on update that requires to

            //Transaction must be declared here because throwing exceptions need to rollback the transaction
            Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? transaction = null;

            dynamic? common = null;

            //set the current context user 
            requestBase.setCurrentContextUser(_contextUser);
            try
            {
                //Disparar el evento
                //_eventBase<TEntity>.FireUpdate();
                //Convert the json to the required class without adding relations (EF way to work)

                var DicAttributes = JsonConvert.DeserializeObject<Dictionary<int, Dictionary<string, int?>>>(bodyRequestBase._attributes);




                //Begin the transaction


                int index = 0;

                foreach (var dic in DicAttributes.Values)
                {
                    transaction = await _dataContext.Database.BeginTransactionAsync();

                    //Add the ordering field with order index to the model's dictionary
                    dic.Add(requestBase.orderingField, index++);

                    //Extract the searching field and value to find the DB object to update
                    var field = dic.Select(dic => dic.Key).FirstOrDefault();

                    var value = dic.Select(dic => dic.Value).FirstOrDefault();



                    var modelToUpdateQuery = _dbSet.Where($"obj => obj.{field} == @0", value);


                    //get the model that will be updated
                    var modelToUpdate = await modelToUpdateQuery.SingleOrDefaultAsync();



                    //if the model is null then throw exception and rollback transaction if not then update
                    if (modelToUpdate != null)
                    {
                        //Remove the id to avoid id changing exception 
                        if (dic.Keys.Contains("id")) dic.Remove("id");

                       _dataContext.Entry(modelToUpdate).CurrentValues.SetValues(dic);

                        //get context Token
                        string? contextToken = requestBase.getCurrentContextToken();

                        //if token is not null get the id of creator from token, if null log the warning
                        if (contextToken != null)
                        {
                            string? userIdstr = await JWTHelper.getJWTTokenClaimAsync(contextToken, "UserId");

                            if (userIdstr != null) modelToUpdate.updated_by = long.Parse(userIdstr);

                        }
                        else
                        {
                            Logger.CoreLogger.LogMessage($"Token for update {typeof(TEntity).Name} object is empty, identity fields will be null",
                                null,
                                Ihelpers.Helpers.LogType.Warning);
                        }


                        try
                        {
                           _dataContext.Entry(modelToUpdate).Property("password").IsModified = false;

                        }
                        catch
                        {
                        }


                        await _dataContext.SaveChangesAsync(CancellationToken.None);

                        await transaction.CommitAsync();


                    }
                    else
                    {
                        throw new ExceptionBase($"Item with {"field"} {requestBase.criteria} ", 404);
                    }

                }



            }
            catch (Exception ex)
            {

                ExceptionBase.HandleException(ex, $"Error updating {typeof(TEntity).Name}", " trace received: " + JsonConvert.SerializeObject(bodyRequestBase._attributes), transaction);

            }

            return common;
        }



        public virtual async Task UpdateProperties(object source, object target, string json)
        {
            JObject jObject = JObject.Parse(json);
            PropertyInfo[] properties = target.GetType().GetProperties();

            foreach (PropertyInfo property in properties)
            {
                JToken value;
                if (jObject.TryGetValue(property.Name, StringComparison.OrdinalIgnoreCase, out value))
                {
                    object newValue = property.GetValue(source);
                    property.SetValue(target, newValue);
                }
            }

           ((EntityBase)target).dynamic_parameters = ((EntityBase)source).dynamic_parameters;
        }
    }
}
