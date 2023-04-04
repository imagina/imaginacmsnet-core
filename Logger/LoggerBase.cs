using Ihelpers.Helpers;
using Idata.Data;
using Idata.Entities.Core;

namespace Core.Logger
{
    public class CoreLogger
    {
        /// <summary>
        /// Logs a message to the logs table
        /// </summary>
        /// <param name="message">Message field</param>
        /// <param name="stackTrace">stacktrace if required</param>
        /// <param name="logType">enum</param>
        public static async void LogMessage(string? message = null, string? stackTrace = null, LogType logType = LogType.Information, long? userId = null)
        {


            Log? common = new();

            common.message = message;

            common.stackTrace = stackTrace;

            common.user_id = userId;

            common.type = Enum.GetName(typeof(LogType), logType);

            //call the task without force await
            Task.Factory.StartNew(() => InsertMessage(common));


        }

        /// <summary>
        /// This internal method inserts the log message into the database, meant to be called inside a different thread 
        /// </summary>
        /// <param name="common"></param>
        private static async void InsertMessage(Log common)
        {
            Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? transaction = null;
            try
            {
                using (IdataContext _dataContext = new IdataContext())
                {

                    //Begin the transaction
                    transaction = await _dataContext.Database.BeginTransactionAsync();

                    //save the model in database and commit transaction
                    await _dataContext.Logs.AddAsync(common);
                    await _dataContext.SaveChangesAsync(CancellationToken.None);
                    await transaction.CommitAsync();

                }

            }
            catch (Exception ex)
            {
                //This exception can only be debugged in debug mode
            }
        }
    }
}
