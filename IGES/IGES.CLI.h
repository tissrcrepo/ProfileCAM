#pragma once

class IGESNative;
namespace ChassisCAM::IGES {
   public ref class IGES {
      public:
      IGES();
      ~IGES();
      !IGES();

      void Initialize();
      void Uninitialize();

      void InitView(System::IntPtr parentWnd);
      void ResizeView();

      void Zoom(bool zoomIn, int x, int y);
      void Pan(int dx, int dy);

      int LoadIGES(System::String^ filePath, int shapeType);
      int SaveIGES(System::String^ filePath, int shapeType);

      int AlignToXYPlane(int shapeType);

      //int GetShape(int shapeType, int width, int height, array<unsigned char>^% rData);

      int YawPartBy180(int order);
      int RollPartBy180(int order);
      
      void Redraw();
      int SaveAsIGS(System::String^ filePath);

      int UnionShapes();
      int UndoJoin();

      void GetErrorMessage([System::Runtime::InteropServices::Out] System::String^% message);

      private:
      IGESNative* pPriv = nullptr;
   };
}