package pt.yaku.booru.szurubooru;

import jakarta.ws.rs.GET;
import jakarta.ws.rs.HeaderParam;
import jakarta.ws.rs.Path;
import jakarta.ws.rs.QueryParam;

@Path("/")
public interface SzurubooruApi {

    @GET
    @Path("api/posts/")
    String listPosts(@HeaderParam("User-Agent") String userAgent,
                     @HeaderParam("Authorization") String authorization,
                     @QueryParam("offset") int offset,
                     @QueryParam("limit") int limit,
                     @QueryParam("query") String query);

    @GET
    @Path("api/tags/")
    String listTags(@HeaderParam("User-Agent") String userAgent,
                    @HeaderParam("Authorization") String authorization,
                    @QueryParam("offset") int offset,
                    @QueryParam("limit") int limit,
                    @QueryParam("query") String query);
}
