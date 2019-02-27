using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using System.IO;

namespace test
{
    class Program
    {
        static void Main(string[] args)
        {
            //TcpServer server = new TcpServer("127.0.0.1", 8080);
            //server.Start(10);

            //byte[] list = Encoding.Unicode.GetBytes("我你AB");
            //foreach (var item in list)
            //{
            //    Console.WriteLine(item.ToString("x"));
            //}


            //byte[] list2 = Encoding.UTF8.GetBytes("我你AB");
            //foreach (var item in list2)
            //{
            //    Console.WriteLine(item.ToString("x"));
            //}



            //string str = Encoding.Unicode.GetString(new byte[] { 0x11,0x62,0x41 });
            //Console.WriteLine(str.Length);
            //Console.WriteLine(str);
            //str = Encoding.UTF8.GetString(new byte[] { 0xe6, 0x88, 0x91,0xe4,0xbd });
            //Console.WriteLine(str.Length);
            //Console.WriteLine(str);

            byte[] list = Encoding.UTF8.GetBytes("\r");
            foreach (var item in list)
            {
                Console.WriteLine(item.ToString("x"));
            }
            list = Encoding.ASCII.GetBytes("\n");
            foreach (var item in list)
            {
                Console.WriteLine(item.ToString("x"));
            }

            Console.Read();
        }
    }
}
