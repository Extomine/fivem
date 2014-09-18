using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security;

using System.Reflection;

namespace CitizenFX.Core
{
    internal static class RuntimeManager
    {
        private static string ms_resourceName;
        private static string ms_resourcePath;
        private static string ms_resourceAssembly;

        private static List<BaseScript> ms_definedScripts = new List<BaseScript>();

        public static void Initialize()
        {
            InitializeResourceInfo();

            Debug.WriteLine("Initializing Mono script environment for {0}.", ms_resourceName);

            try
            {
                // set the assembly resolver
                InitializeAssemblyResolver();

                // configure the task scheduler
                CitizenTaskScheduler.Create();

                //ms_definedScripts.Add(new TestScript());

                // load the main assembly
                var mainAssembly = LoadAssembly(ms_resourceAssembly);

                if (mainAssembly == null)
                {
                    return;
                }

                var definedTypes = mainAssembly.GetTypes();

                foreach (var type in definedTypes)
                {
                    if (type.IsSubclassOf(typeof(BaseScript)))
                    {
                        try
                        {
                            var derivedScript = Activator.CreateInstance(type) as BaseScript;

                            Debug.WriteLine("Instantiated instance of script {0}.", type.FullName);

                            ms_definedScripts.Add(derivedScript);
                        }
                        catch (Exception e)
                        {
                            Debug.WriteLine("Failed to instantiate instance of script {0}: {1}", type.FullName, e.ToString());
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Debug.WriteLine("Fatal error during loading: {0}", e.ToString());

                if (e.InnerException != null)
                {
                    Debug.WriteLine("{0}", e.InnerException.ToString());
                }

                throw e;
            }
        }

        [SecuritySafeCritical]
        static void InitializeResourceInfo()
        {
            string resourceName;
            string resourcePath;
            string resourceAssembly;
            uint instanceId;

            GameInterface.GetEnvironmentInfo(out resourceName, out resourcePath, out resourceAssembly, out instanceId);

            ms_resourcePath = resourcePath;
            ms_resourceAssembly = resourceAssembly;
            ms_resourceName = resourceName;
        }

        #region assembly loading
        [SecuritySafeCritical]
        static void InitializeAssemblyResolver()
        {
            AppDomain.CurrentDomain.AssemblyResolve += CurrentDomain_AssemblyResolve;
        }

        static Assembly CurrentDomain_AssemblyResolve(object sender, ResolveEventArgs args)
        {
            return LoadAssembly(args.Name.Split(',')[0]);
        }

        [SecuritySafeCritical]
        private static Assembly LoadAssembly(string name)
        {
            byte[] assemblyData = null;

            try
            {
                var assemblyFile = new RageFileStream(ms_resourcePath + name);

                assemblyData = new byte[assemblyFile.Length];
                assemblyFile.Read(assemblyData, 0, assemblyData.Length);

                assemblyFile.Close();
            }
            catch (IOException)
            {
                Debug.WriteLine("Could not load assembly {0}.", name);

                return null;
            }

            // read a possible symbol file
            byte[] symbolData = null;

            try
            {
                var symbolFile = new RageFileStream(ms_resourcePath + name + ".mdb");

                symbolData = new byte[symbolFile.Length];
                symbolFile.Read(symbolData, 0, symbolData.Length);

                symbolFile.Close();
            }
            catch (IOException)
            {
                // nothing
            }

            try
            {
                return Assembly.Load(assemblyData, symbolData);
            }
            catch (BadImageFormatException e)
            {
                Debug.WriteLine("Error loading assembly {0}: {1}", name, e.ToString());

                return null;
            }
        }
        #endregion

        private static List<Tuple<DateTime, AsyncCallback>> ms_delays = new List<Tuple<DateTime, AsyncCallback>>();

        public static void Tick()
        {
            var delays = ms_delays.ToArray();
            var now = DateTime.Now;

            foreach (var delay in delays)
            {
                if (now >= delay.Item1)
                {
                    delay.Item2(new DummyAsyncResult());

                    ms_delays.Remove(delay);
                }
            }

            foreach (var script in ms_definedScripts)
            {
                script.ScheduleRun();
            }

            CitizenTaskScheduler.Instance.Tick();
        }

        public static void AddDelay(int delay, AsyncCallback callback)
        {
            ms_delays.Add(Tuple.Create(DateTime.Now.AddMilliseconds(delay), callback));
        }

        public static void TriggerEvent(string eventName, byte[] eventArgs, int eventSource)
        {
            Debug.WriteLine("Triggering event {0}.", eventName);

            try
            {
                dynamic obj = MsgPackDeserializer.Deserialize(eventArgs);

                if (obj == null)
                {
                    return;
                }

                if (eventName == "testingCli")
                {
                    obj[0]((Action<string>)(apex =>
                    {
                        Debug.WriteLine("Hello from CLR: {0}", apex);
                    }));
                }

                foreach (object entry in obj)
                {
                    Debug.WriteLine("{0} {1}", entry.GetType().Name, entry.ToString());
                }
            }
            catch (Exception e)
            {
                Debug.WriteLine(e.ToString());
            }
        }

        public static byte[] CallRef(uint reference, byte[] argsSerialized)
        {
            try
            {
                return FunctionReference.Invoke(reference, argsSerialized);
            }
            catch (Exception e)
            {
                Debug.WriteLine("Exception while calling native reference: {0}", e.ToString());

                return new byte[] { 0xC0 };
            }
        }

        public static void RemoveRef(uint reference)
        {
            FunctionReference.Remove(reference);
        }
    }
}