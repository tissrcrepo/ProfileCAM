#define NOMINMAX // Disable the min/max macros
#include <assert.h>
#include <memory>
#include <vector>
#include <limits>

#include <msclr/marshal_cppstd.h>

#include "priv/IGESNative.h"
#include "IGES.CLI.h"

using namespace System;
using namespace System::Runtime::InteropServices;

// Declare CleanupTCL() as an external function
extern "C" void CleanupOCCT(); //Allows C++/CLI to call it

namespace ChassisCAM::IGES {

   IGES::IGES() : pPriv(nullptr) {}

   IGES::~IGES() {
      this->!IGES();
   }

   IGES::!IGES() { // Finalizer
      if (pPriv) {
         pPriv->Cleanup();  // Ensure cleanup before deleting
         delete pPriv;
         pPriv = nullptr;
      }
      //Call CleanupOCCT() from native C++ layer
      CleanupOCCT();
   }

   void IGES::Initialize() {
      if (!pPriv)
         pPriv = new IGESNative();
   }

   void IGES::Uninitialize() {
      if (!pPriv) return;

      pPriv->Cleanup();
      delete pPriv;
      pPriv = nullptr;

      CleanupOCCT();
   }

   void IGES::ResizeView() {
      assert(this->pPriv);
      this->pPriv->ResizeView();
   }


   void IGES::InitView(System::IntPtr parentWnd) {
      assert(this->pPriv);
      HWND parentHwnd = reinterpret_cast<HWND>(parentWnd.ToPointer());
      this->pPriv->InitView(parentHwnd);
   }

   void IGES::GetErrorMessage([System::Runtime::InteropServices::Out] System::String^% message) {
      message = gcnew String(g_Status.error.data());
   }

   void IGES::Zoom(bool zoomIn, int x, int y) {
      this->pPriv->Zoom(zoomIn, x, y);
   }

   void IGES::Pan(int dx, int dy) {
      this->pPriv->Pan(dx, dy);
   }

   int IGES::LoadIGES(System::String^ filePath, int order) {
      if (!pPriv)
         throw gcnew System::Exception("IGES engine not initialized.");

      std::string stdFilePath = msclr::interop::marshal_as<std::string>(filePath);
      try {
         this->pPriv->LoadIGES(stdFilePath, order);
      }
      catch (const std::exception& ex) {
         throw gcnew System::Exception(gcnew System::String(ex.what()));
      }
      catch (...) {
         throw gcnew System::Exception("An unknown error occurred while loading the part.");
      }
      return 0;
   }

   int IGES::SaveIGES(System::String^ filePath, int order) {
      assert(this->pPriv);

      std::string stdFilePath = msclr::interop::marshal_as<std::string>(filePath);
      return this->pPriv->SaveIGES(stdFilePath, order);
   }

   int IGES::SaveAsIGS(System::String^ filePath) {
      assert(this->pPriv);

      std::string stdFilePath = msclr::interop::marshal_as<std::string>(filePath);
      return pPriv->SaveAsIGS(stdFilePath);
   }

   int IGES::UnionShapes() {
      assert(this->pPriv);
      try {
         pPriv->UnionShapes();
      }
      catch (const NoPartLoadedException& ex) {
         throw gcnew System::Exception(gcnew System::String(ex.what()));
      }
      catch (const FuseFailureException& ex) {
         throw gcnew System::Exception(gcnew System::String(ex.what()));
      }
      catch (const std::exception& ex) { // Catch other standard exceptions
         throw gcnew System::Exception(gcnew System::String(ex.what()));
      }
      catch (...) {
         throw gcnew System::Exception("An unknown error occurred while fusing the parts.");
      }
      return 0;
   }

   int IGES::AlignToXYPlane(int order) {
      assert(this->pPriv);
      return this->pPriv->AlignToXYPlane(order);
   }

   void IGES::Redraw() {
      assert(this->pPriv);
      this->pPriv->Redraw();
   }

   int IGES::YawPartBy180(int pno) {
      assert(this->pPriv);

      try {
         int errorNo = this->pPriv->YawBy180(pno);
         if (0 == errorNo)
            this->Redraw();

         return errorNo;
      }
      catch (const NoPartLoadedException& ex) {
         throw gcnew System::Exception(gcnew System::String(ex.what()));
      }
      catch (const std::exception& ex) { // Catch other standard exceptions
         throw gcnew System::Exception(gcnew System::String(ex.what()));
      }
      catch (...) {
         throw gcnew System::Exception("An unknown error occurred while rotating the part.");
      }
   }

   int IGES::RollPartBy180(int pno) {
      assert(this->pPriv);

      try {
         int errorNo = this->pPriv->RollBy180(pno);
         if (0 == errorNo)
            this->Redraw();

         return errorNo;
      }
      catch (const NoPartLoadedException& ex) {
         throw gcnew System::Exception(gcnew System::String(ex.what()));
      }
      catch (const std::exception& ex) { // Catch other standard exceptions
         throw gcnew System::Exception(gcnew System::String(ex.what()));
      }
      catch (...) {
         throw gcnew System::Exception("An unknown error occurred while rotating the part.");
      }
   }

   int IGES::UndoJoin() {
      assert(this->pPriv);
      int errorNo = this->pPriv->UndoJoin();
      if (0 == errorNo)
         this->Redraw();
      return errorNo;
   }

   //int IGES::GetShape(int shapeType, int width, int height, array<unsigned char>^% rData) {
   //   std::vector<unsigned char> pngData;
   //
   //   // Call the native method to populate pngData
   //   int errorNo = pPriv->GetShape(pngData, shapeType, width, height);
   //
   //   // Handle errors
   //   if (errorNo != 0 || pngData.empty()) {
   //      return errorNo;
   //   }
   //
   //   // Validate pngData size
   //   size_t size = pngData.size();
   //   if (size > static_cast<size_t>(std::numeric_limits<int>::max()))
   //      throw gcnew ArgumentException("Data size is too large.");
   //   

   //   // Allocate managed array to hold the image data
   //   rData = gcnew array<unsigned char>(static_cast<int>(size));

   //   // Use Marshal::Copy to transfer data from native to managed memory
   //   Marshal::Copy(static_cast<IntPtr>(pngData.data()), rData, 0, static_cast<int>(size));
   //
   //   return errorNo;
   //}
}