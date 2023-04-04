using Ihelpers.Helpers;
using Core.Logger;

namespace Core.Exceptions
{

    [Serializable]
    /// <summary>
    /// The `ExceptionBase` class is used as a base class for custom exceptions in the application.
    /// </summary>
    public class ExceptionBase : Exception
    {
        public int CodeResult { get; set; } = 0; // The result code
        public string CustomMessage { get; set; } = ""; // Custom error message
        public object? toReturn { get; set; } = null; // Object to return

        private long? user_id = null; // User ID

        // Default constructor
        public ExceptionBase() { }

        // Constructor with message
        public ExceptionBase(string message) : base(message)
        {

        }

        // Constructor with message, inner exception, and result code
        public ExceptionBase(string message, Exception inner, int codeResult) : base(message, inner)
        {
            CodeResult = codeResult;
        }

        // Constructor with custom message, message, and result code
        public ExceptionBase(string customMessage, string message, int codeResult) : base(message)
        {
            this.CustomMessage = customMessage;
            this.CodeResult = codeResult;
        }

        // Constructor with custom message and result code
        public ExceptionBase(string customMessage, int codeResult)
        {
            CustomMessage = customMessage;
            CodeResult = codeResult;
        }

        // Constructor with custom message, result code, and user ID
        public ExceptionBase(string customMessage, int codeResult, long? _userId)
        {
            CustomMessage = customMessage;
            CodeResult = codeResult;
            user_id = _userId;
            CoreLogger.LogMessage(customMessage, logType: LogType.Exception, userId: _userId);
        }

        // Method to handle exceptions
        public static void HandleException(Exception ex, string customMessage, string? databaseMessage = null, Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? transaction = null, long? userID = null)
        {
            // Try to roll back the current transaction
            if (transaction != null)
            {
                try
                { transaction.Rollback(); }
                catch { }
            }

            // Log the error message in a separate task
            Task.Factory.StartNew(() => CoreLogger.LogMessage(customMessage, databaseMessage + ex.ToString(), LogType.Exception, userID));

            // If the exception was thrown intentionally for us then throw it, if not then create a new BaseException for throw
            if (ex is ExceptionBase)
            {
                throw ex;
            }
            else
            {
                if (ex.Message.Contains("Microsoft.EntityFrameworkCore.Query.InvalidIncludePathError"))
                {
                    throw new ExceptionBase(ex.Message, ex.Message, 400);
                }
                throw new ExceptionBase(customMessage, ex.Message, 500);
            }
        }

        // Method to handle exceptions
        public static void HandleSilentException(Exception ex, string customMessage, string? databaseMessage = null, Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? transaction = null, long? userID = null)
        {
            // Try to roll back the current transaction
            if (transaction != null)
            {
                try
                { transaction.Rollback(); }
                catch { }
            }

            // Log the error message in a separate task
            Task.Factory.StartNew(() => CoreLogger.LogMessage(customMessage, databaseMessage + ex.ToString(), LogType.Exception, userID));

          
        }
        // Method to create a response from an exception
        public object CreateResponseFromException()
        {
            // Create the list of messages to be returned
            List<object> _messages = new List<object>();

            // Create a message to add to the list
            object message = new
            {
                message = $"{this.CustomMessage}",
                type = "error"
            };

            // Add the message to the list
            _messages.Add(message);

            //Create the return object that contains the error messages matching front needs
            object toReturn = new
            {
                messages = _messages
            };
            //return the object
            return toReturn;
        }
    }

}
