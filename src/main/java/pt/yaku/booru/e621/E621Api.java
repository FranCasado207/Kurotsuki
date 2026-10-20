package pt.yaku.booru.e621;

import jakarta.ws.rs.GET;
import jakarta.ws.rs.HeaderParam;
import jakarta.ws.rs.Path;
import jakarta.ws.rs.QueryParam;

@Path("/")
public interface E621Api {

    @GET
    @Path("posts.json")
    String listPosts(@HeaderParam("User-Agent") String userAgent,
                     @HeaderParam("Authorization") String authorization,
                     @QueryParam("tags") String tags,
                     @QueryParam("page") int page,
                     @QueryParam("limit") int limit);

    @GET
    @Path("tags.json")
    String listTags(@HeaderParam("User-Agent") String userAgent,
                    @HeaderParam("Authorization") String authorization,
                    @QueryParam("search[name_matches]") String nameMatches,
                    @QueryParam("search[order]") String order,
                    @QueryParam("limit") int limit);
}
