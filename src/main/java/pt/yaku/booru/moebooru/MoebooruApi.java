package pt.yaku.booru.moebooru;

import jakarta.ws.rs.DefaultValue;
import jakarta.ws.rs.GET;
import jakarta.ws.rs.HeaderParam;
import jakarta.ws.rs.Path;
import jakarta.ws.rs.QueryParam;

@Path("/")
public interface MoebooruApi {

    @GET
    @Path("post.json")
    String listPosts(@HeaderParam("User-Agent") String userAgent,
                     @QueryParam("tags") String tags,
                     @QueryParam("page") int page,
                     @QueryParam("limit") int limit);

    @GET
    @Path("tag.json")
    String listTags(@HeaderParam("User-Agent") String userAgent,
                    @QueryParam("name") String name,
                    @QueryParam("order") @DefaultValue("count") String order,
                    @QueryParam("limit") int limit);

    @GET
    @Path("pool.json")
    String listPools(@HeaderParam("User-Agent") String userAgent,
                     @QueryParam("query") String query,
                     @QueryParam("page") int page);

    /** Returns the pool with all of its posts in reading order; there is no paging. */
    @GET
    @Path("pool/show.json")
    String showPool(@HeaderParam("User-Agent") String userAgent,
                    @QueryParam("id") long id);
}
