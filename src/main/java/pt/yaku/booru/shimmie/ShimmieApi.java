package pt.yaku.booru.shimmie;

import jakarta.ws.rs.GET;
import jakarta.ws.rs.HeaderParam;
import jakarta.ws.rs.Path;
import jakarta.ws.rs.QueryParam;

@Path("/api/danbooru")
public interface ShimmieApi {

    @GET
    @Path("find_posts")
    String listPosts(@HeaderParam("User-Agent") String userAgent,
                     @QueryParam("tags") String tags,
                     @QueryParam("page") int page,
                     @QueryParam("limit") int limit);
}
