using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using log4net;
using System.Threading;

/// <summary>
/// 不支持Unicode编码（全是两个字节的编码）！
/// </summary>
namespace 通信框架
{
    public class TcpServer
    {
        public delegate void RaiseBusinessEventDelegate(BussinessEventArge arge);

        /*业务逻辑绑定在上面*/
        public RaiseBusinessEventDelegate OneBusinessEventDelegate;
        

        #region Field
        private static log4net.ILog log = log4net.LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);
        public string Ip { get; private set; }
        public int Port { get; private set; }
        public Encoding Encoding { get; private set; }

        private Socket socketWatch;

        private bool isRunning;
        private Dictionary<string, MySession> clients = new Dictionary<string, MySession>();

        private const string endMark = "\r\n";
        private const byte backslash_r = 0xd;
        private const byte backslash_n = 0xa;
        public UInt32 recvDataBuffSize = 2048;
        private ReaderWriterLockSlim lockClients = new ReaderWriterLockSlim();

        #endregion

        #region Constructor
        public TcpServer()
        {
            Ip = IPAddress.Any.ToString();
            Port = 8080;
            IPEndPoint point = new IPEndPoint(IPAddress.Any, Port);
            socketWatch = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            socketWatch.Bind(point);
            Encoding = Encoding.UTF8;
        }

        public TcpServer(string ip)
        {
            Ip = ip;
            Port = 8080;
            IPEndPoint point = new IPEndPoint(IPAddress.Parse(Ip), Port);
            socketWatch = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            socketWatch.Bind(point);
            Encoding = Encoding.UTF8;
        }

        public TcpServer(string ip, int port)
        {
            Ip = ip;
            Port = port;
            IPEndPoint point = new IPEndPoint(IPAddress.Parse(Ip), Port);
            socketWatch = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            socketWatch.Bind(point);
            Encoding = Encoding.UTF8;
        }

        public TcpServer(string ip, int port, Encoding encoding)
        {
            Ip = ip;
            Port = port;
            IPEndPoint point = new IPEndPoint(IPAddress.Parse(ip), port);
            socketWatch = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            socketWatch.Bind(point);
            Encoding = encoding;
        }
        #endregion

        #region 提供的服务

        #region 开启服务器
        public void Start(int backlog)
        {
            if (!isRunning)
            {
                isRunning = true;
                socketWatch.Listen(backlog);
                string info = string.Format("{0}:{1} start listening...", this.Ip, this.Port);
                Console.WriteLine(info);
                IAsyncResult result = socketWatch.BeginAccept(HandleAcceptConnected, null);

            }
        }
        #endregion

        #region TODO
        public void Stop()
        {

        }

        public void Send(string data)
        {

        }

        public void Send(byte[] data)
        {

        }

        public void Boracast(string data)
        {

        }

        public void Boracast(byte data)
        {

        }

        #endregion

        #endregion

        #region Accept
        private void HandleAcceptConnected(IAsyncResult ia)
        {
            if (isRunning)
            {
                Socket client = socketWatch.EndAccept(ia);
                string info = string.Format("{0} ----> {1}", client.RemoteEndPoint, client.LocalEndPoint);
                Console.WriteLine(info);
                MySession session = new MySession(client);
                lockClients.EnterWriteLock();
                clients[client.RemoteEndPoint.ToString()] = session;
                lockClients.ExitWriteLock();
                session.recvDataBuff = new byte[1024 * 50];
                IAsyncResult result = client.BeginReceive(session.recvDataBuff, 0, session.recvDataBuff.Length,
                    SocketFlags.None, new AsyncCallback(HandleDataReceived), client.RemoteEndPoint.ToString());
                if (isRunning)
                {
                    socketWatch.BeginAccept(new AsyncCallback(HandleAcceptConnected), null);
                }
            }
        }
        #endregion

        #region Received
        private void HandleDataReceived(IAsyncResult ia)
        {
            if (isRunning)
            {
                string target = (string)ia.AsyncState;
                MySession session = null;
                int length = 0;
                lockClients.EnterReadLock();
                if (clients.Keys.Contains(target))
                {
                    session = clients[target];
                }
                lockClients.ExitReadLock();
                if (session != null)
                {
                    length = session.client.EndReceive(ia);
                }
                if (length == 0)
                {
                    RemoveClient(target);
                    string info = string.Format("client:{0} offline.", session.client.RemoteEndPoint);
                    Console.WriteLine(info);
                }
                else
                {
                    Tuple<MySession, int> transmitter = Tuple.Create<MySession, int>(session, length);
                    //必须同步处理粘包
                    string[] messages = HandleStickPackage(transmitter);
                    //异步触发业务代码
                    new Thread(()=> {
                        if(OneBusinessEventDelegate != null)
                        {
                            OneBusinessEventDelegate(new BussinessEventArge(session.client.RemoteEndPoint.ToString(), messages));
                        }
                    }).Start();

                    session.client.BeginReceive(session.recvDataBuff, 0, session.recvDataBuff.Length, 
                        SocketFlags.None, new AsyncCallback(HandleDataReceived), session.client.RemoteEndPoint.ToString());
                }
            }
        }
        #endregion

        #region 断开与客户端的连接
        public void RemoveClient(string target)
        {
            Socket socket = null;
            lockClients.EnterWriteLock();
            if (clients.Keys.Contains(target))
            {
                socket = clients[target].client;
                clients.Remove(target);
            }
            lockClients.ExitWriteLock();
            if (socket != null)
            {
                socket.Shutdown(SocketShutdown.Both);
                socket.Close();
            }
        }
        #endregion

        #region 处理粘包
        protected virtual string[] HandleStickPackage(Tuple<MySession, int> transmitter)
        {
            string[] messages = null;
            MySession session = transmitter.Item1;
            int length = transmitter.Item2; //本次实际接收到的数据的长度

            List<int> endMarkIndexs = new List<int>();//存放消息结束符在合并数组中出现的位置

            /*扫描新接收的数据中有无结束标志*/
            for (int i = 0; i < length - 1; ++i)
            {
                if (session.recvDataBuff[i] == backslash_r && session.recvDataBuff[i + 1] == backslash_n)
                {
                    endMarkIndexs.Add(i);
                }
            }

            /*新收到的数据无结束标志*/
            if (endMarkIndexs.Count == 0)
            {
                messages = new string[0];
                if (length > session.restCache.Length - session.restCacheElemCount)
                    throw new Exception("存放未解析的数据缓存区溢出");
                else
                {
                    Array.Copy(session.recvDataBuff, 0, session.restCache, session.restCacheElemCount, length);
                    session.restCacheElemCount += length;
                    session.recvDataBuff = null;
                }
            }
            /*新收到的数据有结束标志*/
            else
            {
                messages = new string[endMarkIndexs.Count];
                byte[] rest = GetSubArray(session.restCache, 0, session.restCacheElemCount);
                byte[] FistPart = GetSubArray(session.recvDataBuff, 0, endMarkIndexs[0] + 1);
                byte[] FirstMessage = new byte[rest.Length + FistPart.Length];
                Array.Copy(rest, FirstMessage, rest.Length);
                Array.Copy(FistPart, 0, FirstMessage, rest.Length, FistPart.Length);
                messages[0] = this.Encoding.GetString(FirstMessage);

                for (int k = 0; k < endMarkIndexs.Count - 1; ++k)
                {

                    messages[k + 1] = this.Encoding.GetString(GetSubArray
                        (session.recvDataBuff, endMarkIndexs[k], endMarkIndexs[k + 1] - endMarkIndexs[k]));
                }
                session.recvDataBuff = null;
                
            }
            return messages;
        }

        /// <summary>
        /// 获取一个字节数组的子数组
        /// </summary>
        /// <param name="source"></param>
        /// <param name="startIndex"></param>
        /// <param name="count"></param>
        /// <returns></returns>
        private byte[] GetSubArray(byte[] source, int startIndex, int count)
        {

            byte[] subArray = new byte[count];
            for (int i = 0; i < count; ++i)
            {
                subArray[i] = source[startIndex + i];
            }
            return subArray;
        }
        #endregion

        #region Send
        public void Send(string point, byte[] data)
        {
            if (isRunning)
            {
                if (clients.Keys.Contains(point))
                {
                    clients[point].client.BeginSend(data, 0, data.Length, SocketFlags.None, WorkAfterSendMessage, clients[point]);
                }
            }
        }


        public void Send(string point, string data)
        {
            if (isRunning)
            {
                if (clients.ContainsKey(point))
                {
                    clients[point].client.Send(this.Encoding.GetBytes(data));
                }
            }
        }

        public void Broadcast(byte[] data)
        {
            foreach (string item in clients.Keys)
            {
                this.Send(item, data);
            }
        }

        public void Broadcast(string data)
        {
            foreach (string item in clients.Keys)
            {
                this.Send(item, data);
            }
        }

        #endregion

        #region
        protected void WorkAfterSendMessage(IAsyncResult ia)
        {
            MySession session = (MySession)ia.AsyncState;
            session.client.EndSend(ia);
        }
        #endregion

        #region MySession
        public class MySession
        {
            public Socket client;
            public byte[] recvDataBuff;
            public byte[] restCache;
            private int restCacheSize;
            public int restCacheElemCount;
            public MySession(Socket client)
            {
                this.client = client;
                restCacheSize = 1024;
                this.restCache = new byte[1024];
                restCacheElemCount = 0;
            }
        }
        #endregion

    }

    #region BussinessEventArge
    public class BussinessEventArge
    {
        public string point;
        public string[] messages;
        public BussinessEventArge(string point,string[] messages)
        {
            this.point = point;
            this.messages = messages;
        }
    }
    #endregion
}

