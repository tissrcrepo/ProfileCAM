namespace ProfileCAM.Core {
   // Exception for negative Z-axis related errors
   public class NegZException : Exception {
      public NegZException ()
           : base ("Negative Z axis feature normal encountered.") { }

      public NegZException (string message)
          : base (message) { }

      public NegZException (string message, Exception innerException)
          : base (message, innerException) { }
   }

   // Exception for notch creation failures
   public class NotchCreationFailedException : Exception {
      public NotchCreationFailedException () { }

      public NotchCreationFailedException (string message)
          : base (message) { }

      public NotchCreationFailedException (string message, Exception innerException)
          : base (message, innerException) { }
   }

   // Exception for infeasible cutouts
   public class InfeasibleCutoutException : Exception {
      public InfeasibleCutoutException () { }

      public InfeasibleCutoutException (string message)
          : base (message) { }

      public InfeasibleCutoutException (string message, Exception innerException)
          : base (message, innerException) { }
   }

   public class FrameNotProcessableException : Exception {
      public FrameNotProcessableException () { }

      public FrameNotProcessableException (string message)
          : base (message) { }

      public FrameNotProcessableException (string message, Exception innerException)
          : base (message, innerException) { }
   }

   public class FrameEmptyException : Exception {
      public FrameEmptyException () { }

      public FrameEmptyException (string message)
          : base (message) { }

      public FrameEmptyException (string message, Exception innerException)
          : base (message, innerException) { }
   }
}