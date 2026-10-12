// Test-only diagnostics for MSVC Debug builds. A CRT assertion or abort would otherwise show a
// modal dialog and hang a headless CI machine. This file is compiled into the E2E native module
// only and is not part of the shipped runtime headers.
#if defined(_MSC_VER) && defined(_DEBUG)

#include <crtdbg.h>
#include <cstdlib>

namespace
{
struct CrtDiagnosticsInitializer
{
    CrtDiagnosticsInitializer() noexcept
    {
        _set_abort_behavior(0, _WRITE_ABORT_MSG | _CALL_REPORTFAULT);
        _set_error_mode(_OUT_TO_STDERR);

        const int reportTypes[] = { _CRT_WARN, _CRT_ERROR, _CRT_ASSERT };
        for (const int reportType : reportTypes)
        {
            _CrtSetReportMode(reportType, _CRTDBG_MODE_FILE);
            _CrtSetReportFile(reportType, _CRTDBG_FILE_STDERR);
        }
    }
};

const CrtDiagnosticsInitializer g_crtDiagnosticsInitializer;
} // namespace

#endif
