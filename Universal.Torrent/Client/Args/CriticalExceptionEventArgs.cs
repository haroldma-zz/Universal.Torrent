using System;

namespace Universal.Torrent.Client.Args
{
    public class CriticalExceptionEventArgs : System.EventArgs
    {
        public CriticalExceptionEventArgs(Exception ex, ClientEngine engine)
        {
            if (ex == null)
                throw new ArgumentNullException(nameof(ex));
            if (engine == null)
                throw new ArgumentNullException(nameof(engine));

            Engine = engine;
            Exception = ex;
        }


        public ClientEngine Engine { get; }

        public Exception Exception { get; }
    }
}