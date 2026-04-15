using System.Reflection;

namespace ProfileCAM.Core.AssemblyUtils {
   public static class AssemblyLoader {
      public static bool IsAssemblyLoadable (string assemblyName) {
         try {
            Assembly.Load (assemblyName);
            return true;
         } catch {
            return false;
         }
      }
   }
}
