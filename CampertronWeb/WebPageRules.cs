using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace CampertronWeb
{
    // A page answers only through one of its own handlers (decision D92). Razor Pages shows a page without running any of
    // its code when the request's method has no handler on it (a POST to a page that only reads), and each page's own
    // first step (where this request belongs: the setup, the sign-in, the start page) would be skipped. Such a request is
    // refused instead: 405 for a POST, 404 otherwise. (A handler name the page does not have, "?handler=anything", is no
    // such case: Razor Pages then runs the page's own handler for the method.)
    internal sealed class WebPageRules : IPageFilter
    {
        public void OnPageHandlerSelected(PageHandlerSelectedContext Context)
        {
        }

        public void OnPageHandlerExecuting(PageHandlerExecutingContext Context)
        {
            if (Context.HandlerMethod == null)
            {
                Context.Result = new StatusCodeResult(HttpMethods.IsPost(Context.HttpContext.Request.Method) ? StatusCodes.Status405MethodNotAllowed : StatusCodes.Status404NotFound);
            }
        }

        public void OnPageHandlerExecuted(PageHandlerExecutedContext Context)
        {
        }
    }
}
